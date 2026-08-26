using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace GeotechSlopeApp.Backend;

/// <summary>
/// Locked-license startup gate. Compiled into the binary only when the project is built with
/// <c>-p:LockedLicense=true</c> (which defines the <c>LOCKED_LICENSE</c> symbol). The class
/// itself always compiles so it can be unit-tested or exercised manually, but
/// <see cref="App.OnStartup"/> only invokes it under <c>#if LOCKED_LICENSE</c>.
///
/// Enforcement model (intentionally lightweight, not real DRM):
///   1. The plaintext "username:password:salt" is hashed with SHA-256 at build time and the
///      hex digest is embedded as a constant. The runtime hashes the user-entered credentials
///      with the same salt and constant-time-compares the digests.
///   2. A fixed UTC expiry timestamp is embedded as a constant. The gate refuses to start
///      once <c>DateTime.UtcNow</c> passes that timestamp.
///   3. To resist a casual clock-rollback attack the gate also stores the most recent
///      observed launch time under <c>HKCU\Software\GeotechSlopeApp\License</c> and refuses
///      if the system clock has moved noticeably backward since the last successful launch.
/// </summary>
public static class LicenseGate
{
    /// <summary>Embedded SHA-256(username:password:salt) digest; lower-case hex.</summary>
    private const string ExpectedCredentialHashHex =
        "94571444d4cbb22aec85193062754e98415598bf37e86fbd8d4cb4256b033b5a";

    /// <summary>Salt used when hashing the credentials. Constant per-build.</summary>
    private const string CredentialSalt = "b166eef4378345f5be7b7d807bbde2ca";

    /// <summary>License expiry, embedded at build time. ISO-8601 UTC.</summary>
    private const string ExpiryUtcIso = "2026-07-09T23:59:59Z";

    /// <summary>Friendly licensee identifier displayed on the login dialog.</summary>
    public const string LicenseeDisplayName = "ramandeep";

    /// <summary>Maximum number of failed login attempts before the gate gives up.</summary>
    private const int MaxAttempts = 3;

    /// <summary>Tolerance for clock-rollback detection (15 minutes covers DST/NTP jitter).</summary>
    private static readonly TimeSpan ClockRollbackTolerance = TimeSpan.FromMinutes(15);

    private const string RegistryKeyPath = @"Software\GeotechSlopeApp\License";
    private const string RegistryValueName = "LastSeenUtc";

    /// <summary>Result of a license verification attempt.</summary>
    public enum VerificationOutcome
    {
        Authorized,
        Expired,
        ClockTampered,
        AuthFailed,
        Cancelled,
    }

    /// <summary>UTC expiry parsed from the embedded constant.</summary>
    public static DateTime ExpiryUtc => DateTime.Parse(
        ExpiryUtcIso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    /// <summary>True if today's UTC date is past the embedded expiry.</summary>
    public static bool IsExpired(DateTime utcNow) => utcNow > ExpiryUtc;

    /// <summary>
    /// Run the full license check at app startup.
    /// Returns <c>true</c> when the user is authorized and startup should continue.
    /// On any failure path the gate calls <see cref="Application.Shutdown(int)"/> on
    /// <paramref name="app"/> and returns <c>false</c> so the caller can skip the rest
    /// of <see cref="Application.OnStartup"/> (in particular, must not call
    /// <c>base.OnStartup</c>, which would otherwise try to open the StartupUri window
    /// after Shutdown has already been requested).
    /// </summary>
    public static bool TryAuthorize(Application app)
    {
        if (app is null) throw new ArgumentNullException(nameof(app));

        var now = DateTime.UtcNow;

        if (IsExpired(now))
        {
            ShowFatal(
                "License expired",
                $"This locked-license build expired on {ExpiryUtc:yyyy-MM-dd HH:mm:ss} UTC.\n" +
                "Please request a renewed build from the application owner.");
            app.Shutdown(2);
            return false;
        }

        if (DetectClockRollback(now))
        {
            ShowFatal(
                "License clock check failed",
                "The system clock appears to have been moved backward since the last successful launch. " +
                "Please correct the system date/time and try again.");
            app.Shutdown(3);
            return false;
        }

        var loginOutcome = PromptForCredentials();
        switch (loginOutcome)
        {
            case VerificationOutcome.Authorized:
                TryUpdateLastSeen(now);
                return true;

            case VerificationOutcome.AuthFailed:
                ShowFatal(
                    "Login failed",
                    $"Authentication failed after {MaxAttempts} attempts. The application will now exit.");
                app.Shutdown(4);
                return false;

            case VerificationOutcome.Cancelled:
            default:
                app.Shutdown(1);
                return false;
        }
    }

    private static VerificationOutcome PromptForCredentials()
    {
        for (int attempt = 1; attempt <= MaxAttempts; ++attempt)
        {
            var dialog = new Views.Dialogs.LoginDialog(attempt, MaxAttempts, ExpiryUtc, LicenseeDisplayName);
            var result = dialog.ShowDialog();

            if (result != true)
                return VerificationOutcome.Cancelled;

            if (VerifyCredentials(dialog.EnteredUsername, dialog.EnteredPassword))
                return VerificationOutcome.Authorized;
        }
        return VerificationOutcome.AuthFailed;
    }

    /// <summary>Constant-time SHA-256 credential verification against the embedded digest.</summary>
    public static bool VerifyCredentials(string? username, string? password)
    {
        if (username is null || password is null) return false;

        var plaintext = string.Concat(username.Trim(), ":", password, ":", CredentialSalt);
        var computed = Sha256Hex(plaintext);
        return ConstantTimeEquals(computed, ExpectedCredentialHashHex);
    }

    private static string Sha256Hex(string s)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; ++i) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private static bool DetectClockRollback(DateTime utcNow)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: false);
            if (key?.GetValue(RegistryValueName) is string raw &&
                DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out var lastSeen))
            {
                return utcNow + ClockRollbackTolerance < lastSeen;
            }
        }
        catch
        {
        }
        return false;
    }

    private static void TryUpdateLastSeen(DateTime utcNow)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
            key?.SetValue(RegistryValueName, utcNow.ToString("o", CultureInfo.InvariantCulture), RegistryValueKind.String);
        }
        catch
        {
        }
    }

    private static void ShowFatal(string title, string body)
    {
        MessageBox.Show(body, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>Build identifier surfaced on the login dialog so support can confirm the locked release.</summary>
    public static string BuildSignature
    {
        get
        {
            var asm = Assembly.GetExecutingAssembly();
            var version = asm.GetName().Version?.ToString() ?? "0.0.0.0";
            var location = asm.Location;
            string? buildDate = null;
            try
            {
                if (!string.IsNullOrEmpty(location) && File.Exists(location))
                    buildDate = File.GetLastWriteTimeUtc(location).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            catch
            {
            }
            return buildDate is null ? $"v{version}" : $"v{version} ({buildDate})";
        }
    }
}
