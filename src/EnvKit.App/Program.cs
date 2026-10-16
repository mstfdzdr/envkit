using System.IO;
using EnvKit.App.Localization;
using EnvKit.App.Settings;
using EnvKit.Core.Plans;
using EnvKit.Core.Snapshots;
using EnvKit.Core.Variables;
using EnvKit.Windows.Elevation;
using EnvKit.Windows.Security;
using EnvKit.Windows.Variables;
using Velopack;

namespace EnvKit.App;

public static class Program
{
    /// <summary>The installer puts the single-file CLI here, alone, so only it lands on PATH.</summary>
    private static string CliDirectory => Path.Combine(AppContext.BaseDirectory, "bin");

    /// <summary>Set when this start is the relaunch right after an update, so the app can say so.</summary>
    public static string? UpdatedTo { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        // Handles the installer's hook invocations and exits; a normal start returns immediately.
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => UpdateCliPath(add: true))
            .OnBeforeUninstallFastCallback(_ => UpdateCliPath(add: false))
            .OnRestarted(version => UpdatedTo = version.ToString())
            .Run();

        // Relaunched through UAC by WindowsElevation: apply the plan and exit without a window.
        if (args is [ElevatedPlanHost.Verb, var planFile, ElevatedPlanHost.HashOption, var hash])
        {
            return ElevatedPlanHost.Run(planFile, hash, new RegistryEnvStore());
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    /// <summary>
    /// Goes through a plan like any other change, so the install and uninstall edits to PATH
    /// show up in History and can be undone.
    /// </summary>
    private static void UpdateCliPath(bool add)
    {
        try
        {
            var store = new RegistryEnvStore();
            var builder = new PlanBuilder(store);
            var changed = add
                ? builder.AddPathEntry(CliDirectory, EnvScope.User)
                : builder.RemovePathEntry(CliDirectory, EnvScope.User) > 0;
            if (changed)
            {
                // The description is stored with the snapshot, so it is written in the language History will show.
                Loc.Instance.SetCulture(AppInfo.Resolve(new FileSettingsStore(FileSettingsStore.DefaultPath).Load().Language));
                var plan = builder.Build(Loc.Get(add ? "Install_AddToPath" : "Uninstall_RemoveFromPath"));
                new PlanApplier(store, new FileSnapshotStore(FileSnapshotStore.DefaultDirectory, new DpapiProtector()), new SettingChangeBroadcaster())
                    .Apply(plan);
            }
        }
#pragma warning disable CA1031 // Never fail an install or uninstall over PATH; the user can add it later.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
