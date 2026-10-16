using System.Text;
using EnvKit.Core.DotEnv;

namespace EnvKit.Core.Tests.DotEnv;

public class DotEnvDocumentTests
{
    private const string Sample =
        "# Database\r\n" +
        "DB_HOST=localhost\r\n" +
        "export DB_PORT = 5432   # default port\r\n" +
        "\r\n" +
        "API_KEY='abc#def'\r\n" +
        "GREETING=\"hello \\\"world\\\"\\nsecond line\"\r\n" +
        "PRIVATE_KEY=\"-----BEGIN-----\r\nline2\r\n-----END-----\"\r\n" +
        "URL=https://example.com/#anchor\r\n" +
        "EMPTY=\r\n" +
        "this line is broken\r\n" +
        "  INDENTED=yes\r\n" +
        "LAST=no-newline";

    private static DotEnvDocument Doc(string text = Sample) => DotEnvDocument.Parse(text);

    [Theory]
    [InlineData(Sample)]
    [InlineData("")]
    [InlineData("A=1\nB=2\n")]
    [InlineData("A=\"unterminated\nB=2\n")]
    [InlineData("\n\n# only comments\n")]
    public void Round_trips_exactly(string text) => Assert.Equal(text, Doc(text).ToText());

    [Fact]
    public void Parses_values_the_way_dotenv_loaders_do()
    {
        var values = Doc().Values().ToDictionary();

        Assert.Equal("localhost", values["DB_HOST"]);
        Assert.Equal("5432", values["DB_PORT"]);
        Assert.Equal("abc#def", values["API_KEY"]);
        Assert.Equal("hello \"world\"\nsecond line", values["GREETING"]);
        Assert.Equal("-----BEGIN-----\r\nline2\r\n-----END-----", values["PRIVATE_KEY"]);
        Assert.Equal("https://example.com/#anchor", values["URL"]);
        Assert.Equal(string.Empty, values["EMPTY"]);
        Assert.Equal("yes", values["INDENTED"]);
        Assert.Equal("no-newline", values["LAST"]);
    }

    [Fact]
    public void Reports_invalid_lines_with_their_line_numbers()
    {
        var invalid = Assert.Single(Doc().InvalidLines);

        Assert.Equal(12, invalid.LineNumber);
        Assert.Equal(13, Doc().Entries.Single(e => e.Key == "INDENTED").LineNumber);
    }

    [Fact]
    public void Setting_a_value_rewrites_only_that_value()
    {
        var doc = Doc();
        doc.Set("DB_PORT", "6543");

        Assert.Equal(Sample.Replace("export DB_PORT = 5432   # default port", "export DB_PORT = 6543   # default port", StringComparison.Ordinal), doc.ToText());
    }

    [Theory]
    [InlineData("API_KEY", "new'quote", "API_KEY=\"new'quote\"")]
    [InlineData("API_KEY", "plain", "API_KEY='plain'")]
    [InlineData("DB_HOST", "has #hash", "DB_HOST=\"has #hash\"")]
    [InlineData("DB_HOST", " padded ", "DB_HOST=\" padded \"")]
    [InlineData("DB_HOST", "two\nlines", "DB_HOST=\"two\\nlines\"")]
    [InlineData("DB_HOST", "back\\slash", "DB_HOST=back\\slash")]
    public void Quoting_is_kept_when_possible_and_added_when_needed(string key, string value, string expectedLine)
    {
        var doc = Doc();
        doc.Set(key, value);

        Assert.Contains(expectedLine, doc.ToText(), StringComparison.Ordinal);
        Assert.Equal(value, DotEnvDocument.Parse(doc.ToText()).Get(key));
    }

    [Fact]
    public void New_keys_are_appended_keeping_the_missing_final_newline()
    {
        var doc = Doc();
        doc.Set("NEW", "1");

        Assert.EndsWith("LAST=no-newline\r\nNEW=1", doc.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void New_keys_use_the_files_line_ending()
    {
        var doc = Doc("A=1\n");
        doc.Set("B", "2");

        Assert.Equal("A=1\nB=2\n", doc.ToText());
    }

    [Fact]
    public void A_trailing_comment_without_newline_gets_one_before_the_new_key()
    {
        var doc = Doc("A=1\n# end");
        doc.Set("B", "2");

        Assert.Equal("A=1\n# end\nB=2", doc.ToText());
    }

    [Fact]
    public void Last_assignment_wins_and_duplicates_are_reported()
    {
        var doc = Doc("A=1\nB=x\nA=2\n");

        Assert.Equal("2", doc.Get("A"));
        Assert.Equal(["A"], doc.DuplicateKeys);

        doc.Set("A", "3");
        Assert.Equal("A=1\nB=x\nA=3\n", doc.ToText());
    }

    [Fact]
    public void Remove_deletes_every_assignment_of_the_key()
    {
        var doc = Doc("A=1\nB=x\nA=2\n");

        Assert.Equal(2, doc.Remove("A"));
        Assert.Equal("B=x\n", doc.ToText());
        Assert.Equal(0, doc.Remove("A"));
    }

    [Fact]
    public void Rename_keeps_the_rest_of_the_line()
    {
        var doc = Doc();
        doc.Rename("DB_PORT", "DATABASE_PORT");

        Assert.Contains("export DATABASE_PORT = 5432   # default port\r\n", doc.ToText(), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => doc.Rename("DB_HOST", "API_KEY"));
        Assert.Throws<ArgumentException>(() => doc.Rename("DB_HOST", "1BAD"));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("_a.b-c9")]
    public void Accepts_valid_keys(string key) => Assert.True(DotEnvDocument.IsValidKey(key));

    [Theory]
    [InlineData("")]
    [InlineData("1A")]
    [InlineData("A B")]
    [InlineData("A=B")]
    public void Rejects_invalid_keys(string key)
    {
        Assert.False(DotEnvDocument.IsValidKey(key));
        Assert.Throws<ArgumentException>(() => Doc("").Set(key, "x"));
    }

    [Fact]
    public void Inline_hash_without_whitespace_is_part_of_the_value() =>
        Assert.Equal("#not-a-comment", Doc("A=#not-a-comment\n").Get("A"));

    [Fact]
    public void Hash_after_whitespace_starts_a_comment() =>
        Assert.Equal(string.Empty, Doc("A= # just a comment\n").Get("A"));

    [Fact]
    public void Byte_order_marks_and_encodings_round_trip()
    {
        var utf8Bom = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("A=ş\n")).ToArray();
        var utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("A=1\r\n")).ToArray();

        Assert.Equal(utf8Bom, DotEnvFile.Parse(utf8Bom).ToBytes());
        Assert.Equal(utf16, DotEnvFile.Parse(utf16).ToBytes());
        Assert.Equal("ş", DotEnvFile.Parse(utf8Bom).Document.Get("A"));

        var edited = DotEnvFile.Parse(utf8Bom);
        edited.Document.Set("B", "2");
        Assert.Equal(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("A=ş\nB=2\n")).ToArray(), edited.ToBytes());
    }

    [Fact]
    public void Invalid_utf8_is_refused() =>
        Assert.Throws<InvalidDataException>(() => DotEnvFile.Parse([(byte)'A', (byte)'=', 0xC3, 0x28]));
}
