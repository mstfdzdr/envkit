using System.Globalization;
using EnvKit.Cli;

// EnvKit's own messages are English; keep System.CommandLine's help and errors in the same language.
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

return EnvKitCli.Run(CliServices.CreateDefault(), args);
