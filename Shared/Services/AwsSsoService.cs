using System.Diagnostics;

namespace KubaToolKit.Shared.Services;

public static class AwsSsoService
{
    public static async Task<bool>
    Login()
    {
        try
        {
            var process = Process.Start
                (new ProcessStartInfo
                    {
                        FileName = "aws",
                        Arguments = "sso login --sso-session kuba-sso",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle =
                        ProcessWindowStyle.Hidden
                    });

            if (process == null)
            {
                return false;
            }

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task
    EnsureLoggedIn(
        string profile)
    {
        try
        {
            var process =
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = "aws",
                        Arguments =
                              $"sts get-caller-identity --profile {profile}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle =
                            ProcessWindowStyle.Hidden
                    });

            if (process == null)
            {
                return;
            }

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                await Login();
            }
        }
        catch
        {
        }
    }

    public static bool
    IsSsoExpired(Exception ex)
    {
        var text = ex.ToString();
        return
            text.Contains("No valid token", StringComparison.OrdinalIgnoreCase)
            || text.Contains("token has expired", StringComparison.OrdinalIgnoreCase)
            || text.Contains("SSO", StringComparison.OrdinalIgnoreCase)
            || text.Contains("aws sso", StringComparison.OrdinalIgnoreCase)
            || text.Contains("login", StringComparison.OrdinalIgnoreCase);
    }
}
