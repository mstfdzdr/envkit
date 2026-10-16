using EnvKit.Core.Plans;
using EnvKit.Core.Snapshots;
using EnvKit.Core.Tests.Fakes;
using EnvKit.Core.Variables;

namespace EnvKit.Core.Tests.Plans;

public class PlanBuilderTests
{
    private readonly InMemoryEnvStore _store = new();

    private PlanBuilder Builder() => new(_store);

    private static VariableChange Single(ChangePlan plan) => Assert.IsType<VariableChange>(Assert.Single(plan.Changes));

    [Fact]
    public void Creating_a_variable_has_no_before_state()
    {
        var change = Single(Builder().Set("NEW", EnvScope.User, "1").Build("t"));

        Assert.Null(change.Before);
        Assert.Equal(new VariableValue("1", EnvValueKind.Literal), change.After);
    }

    [Fact]
    public void Setting_the_current_value_is_not_a_change()
    {
        _store.Set(new EnvVariable("X", "1", EnvScope.User));

        Assert.True(Builder().Set("X", EnvScope.User, "1").Build("t").IsEmpty);
    }

    [Fact]
    public void Edits_that_return_to_the_start_cancel_out()
    {
        _store.Set(new EnvVariable("X", "1", EnvScope.User));

        var plan = Builder().Set("X", EnvScope.User, "2").Set("x", EnvScope.User, "1").Build("t");

        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void Repeated_edits_collapse_and_keep_the_original_before_state()
    {
        _store.Set(new EnvVariable("X", "1", EnvScope.User));

        var change = Single(Builder().Set("X", EnvScope.User, "2").Set("X", EnvScope.User, "3").Build("t"));

        Assert.Equal("1", change.Before?.Value);
        Assert.Equal("3", change.After?.Value);
    }

    [Fact]
    public void Keeps_stored_name_spelling()
    {
        _store.Set(new EnvVariable("Path", @"C:\a", EnvScope.User));

        Assert.Equal("Path", Single(Builder().Set("PATH", EnvScope.User, @"C:\b").Build("t")).Name);
    }

    [Fact]
    public void Kind_is_kept_inferred_or_explicit()
    {
        _store.Set(new EnvVariable("EXP", "%A%", EnvScope.User, EnvValueKind.Expandable));

        var plan = Builder()
            .Set("EXP", EnvScope.User, "now plain")
            .Set("REF", EnvScope.User, @"%USERPROFILE%\x")
            .Set("FORCED", EnvScope.User, "%LITERAL%", EnvValueKind.Literal)
            .Build("t");
        var after = plan.Changes.Cast<VariableChange>().ToDictionary(c => c.Name, c => c.After!.Kind);

        Assert.Equal(EnvValueKind.Expandable, after["EXP"]);
        Assert.Equal(EnvValueKind.Expandable, after["REF"]);
        Assert.Equal(EnvValueKind.Literal, after["FORCED"]);
    }

    [Fact]
    public void Deleting_a_missing_variable_is_not_a_change() =>
        Assert.True(Builder().Delete("NOPE", EnvScope.User).Build("t").IsEmpty);

    [Fact]
    public void Deleting_records_the_value_being_removed()
    {
        _store.Set(new EnvVariable("X", "1", EnvScope.User));

        var change = Single(Builder().Delete("X", EnvScope.User).Build("t"));

        Assert.Equal("1", change.Before?.Value);
        Assert.Null(change.After);
    }

    [Fact]
    public void Adds_path_entries_at_either_end_and_skips_existing_ones()
    {
        _store.Set(new EnvVariable("Path", @"C:\a", EnvScope.User, EnvValueKind.Expandable));
        var builder = Builder();

        Assert.True(builder.AddPathEntry(@"C:\z", EnvScope.User));
        Assert.True(builder.AddPathEntry(@"C:\first", EnvScope.User, prepend: true));
        Assert.False(builder.AddPathEntry(@"c:\A\", EnvScope.User));

        Assert.Equal(@"C:\first;C:\a;C:\z", Single(builder.Build("t")).After?.Value);
    }

    [Fact]
    public void Adding_to_a_missing_path_creates_it()
    {
        var builder = Builder();
        builder.AddPathEntry(@"%USERPROFILE%\bin", EnvScope.User);

        var change = Single(builder.Build("t"));

        Assert.Null(change.Before);
        Assert.Equal(new VariableValue(@"%USERPROFILE%\bin", EnvValueKind.Expandable), change.After);
    }

    [Fact]
    public void Editing_a_REG_SZ_path_with_references_makes_it_expandable()
    {
        _store.Set(new EnvVariable("Path", @"%SystemRoot%\system32", EnvScope.Machine));
        var builder = Builder();
        builder.AddPathEntry(@"C:\tools", EnvScope.Machine);

        Assert.Equal(EnvValueKind.Expandable, Single(builder.Build("t")).After?.Kind);
    }

    [Fact]
    public void Removes_every_matching_path_entry()
    {
        _store.Set(new EnvVariable("Path", @"C:\a;C:\b;c:\a\;C:\c", EnvScope.User));
        var builder = Builder();

        Assert.Equal(2, builder.RemovePathEntry(@"C:\a", EnvScope.User));
        Assert.Equal(0, builder.RemovePathEntry(@"C:\nope", EnvScope.User));
        Assert.Equal(@"C:\b;C:\c", Single(builder.Build("t")).After?.Value);
    }

    [Fact]
    public void Machine_changes_require_elevation()
    {
        Assert.False(Builder().Set("X", EnvScope.User, "1").Build("t").RequiresElevation);
        Assert.True(Builder().Set("X", EnvScope.Machine, "1").Build("t").RequiresElevation);
    }

    [Fact]
    public void Rejects_values_too_long_to_write() =>
        Assert.Throws<ArgumentException>(
            () => Builder().Set("X", EnvScope.User, new string('a', EnvVariable.MaxValueLength + 1)));

    [Fact]
    public void Restore_reverts_edits_creations_and_deletions()
    {
        _store.Set(new EnvVariable("KEPT", "1", EnvScope.User));
        _store.Set(new EnvVariable("EDITED", "old", EnvScope.User, EnvValueKind.Expandable));
        _store.Set(new EnvVariable("DELETED", "x", EnvScope.User));
        _store.Set(new EnvVariable("MACHINE", "m", EnvScope.Machine));
        var snapshot = Snapshot.Capture(_store, "t");

        _store.Set(new EnvVariable("EDITED", "new", EnvScope.User));
        _store.Delete("DELETED", EnvScope.User);
        _store.Set(new EnvVariable("CREATED", "y", EnvScope.User));
        _store.Set(new EnvVariable("MACHINE", "changed", EnvScope.Machine));

        var changes = Builder().Restore(snapshot, EnvScope.User).Build("restore")
            .Changes.Cast<VariableChange>().ToDictionary(c => c.Name);

        Assert.Equal(["CREATED", "DELETED", "EDITED"], changes.Keys.Order());
        Assert.Null(changes["CREATED"].After);
        Assert.Equal("x", changes["DELETED"].After?.Value);
        Assert.Equal(new VariableValue("old", EnvValueKind.Expandable), changes["EDITED"].After);
    }
}
