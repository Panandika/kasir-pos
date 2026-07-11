using System;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Kasir.Data.Repositories;
using Kasir.Models;

namespace Kasir.Auth
{
    public class AuthService
    {
        // F34: lockout state is PERSISTED in the config table so it survives an app
        // restart (previously an attacker could reset the counter by relaunching, and
        // LoginView news up a fresh AuthService on every navigation). The cumulative
        // failure count is never reset on lockout, so each further failure escalates the
        // lockout duration. An in-session MONOTONIC deadline (Environment.TickCount64)
        // is enforced alongside the persisted wall-clock deadline so that moving the
        // system clock forward cannot shorten an active lockout.
        private const string CfgFailCount = "auth.fail_count";
        private const string CfgLockoutUntil = "auth.lockout_until"; // ISO-8601 wall clock
        private const int LockoutThreshold = 3;

        private readonly UserRepository _userRepo;
        private readonly ConfigRepository _config;
        private readonly Func<DateTime> _now;
        private readonly Func<long> _monotonicMs;
        private User _currentUser;
        private long _monoLockoutDeadlineMs; // in-memory monotonic deadline; 0 = none

        public AuthService(SqliteConnection db)
            : this(db, () => DateTime.Now, () => Environment.TickCount64)
        {
        }

        // Testable seam: inject wall clock + monotonic millisecond source.
        public AuthService(SqliteConnection db, Func<DateTime> now, Func<long> monotonicMs)
        {
            _userRepo = new UserRepository(db);
            _config = new ConfigRepository(db);
            _now = now;
            _monotonicMs = monotonicMs;
        }

        public User CurrentUser
        {
            get { return _currentUser; }
        }

        public bool IsLoggedIn
        {
            get { return _currentUser != null; }
        }

        private int FailCount
        {
            get { return int.TryParse(_config.Get(CfgFailCount), out int n) ? n : 0; }
            set { _config.Set(CfgFailCount, value.ToString(CultureInfo.InvariantCulture)); }
        }

        private DateTime WallLockoutUntil
        {
            get
            {
                string s = _config.Get(CfgLockoutUntil);
                if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
                return DateTime.TryParse(s, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime dt) ? dt : DateTime.MinValue;
            }
        }

        public bool IsLockedOut
        {
            get
            {
                // Locked if EITHER the persisted wall deadline (survives restart) OR the
                // in-session monotonic deadline (resists clock manipulation) is in future.
                if (_now() < WallLockoutUntil) return true;
                if (_monoLockoutDeadlineMs > 0 && _monotonicMs() < _monoLockoutDeadlineMs) return true;
                return false;
            }
        }

        public int RemainingLockoutSeconds
        {
            get
            {
                if (!IsLockedOut) return 0;
                long wallSecs = (long)Math.Max(0, (WallLockoutUntil - _now()).TotalSeconds);
                long monoSecs = _monoLockoutDeadlineMs > 0
                    ? Math.Max(0, (_monoLockoutDeadlineMs - _monotonicMs()) / 1000)
                    : 0;
                return (int)Math.Max(wallSecs, monoSecs);
            }
        }

        // 3 failures -> 30s, then doubles per additional failure, capped at 15 minutes.
        private static int LockoutSecondsFor(int failCount)
        {
            int over = failCount - LockoutThreshold;
            if (over < 0) return 0;
            long secs = 30L << Math.Min(over, 5); // 30,60,120,240,480,960
            return (int)Math.Min(secs, 900);
        }

        public LoginResult Login(string username, string password)
        {
            if (IsLockedOut)
            {
                return new LoginResult
                {
                    Success = false,
                    ErrorMessage = string.Format(
                        "Account locked. Try again in {0} seconds.",
                        RemainingLockoutSeconds)
                };
            }

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            {
                return new LoginResult
                {
                    Success = false,
                    ErrorMessage = "Username and password are required."
                };
            }

            var user = _userRepo.GetByUsername(username.Trim().ToUpper());

            if (user == null || user.IsActive == 0)
            {
                IncrementFailedAttempts();
                return new LoginResult
                {
                    Success = false,
                    ErrorMessage = "Invalid username or password."
                };
            }

            bool verified = VerifyPassword(password, user.PasswordHash);

            if (!verified)
            {
                IncrementFailedAttempts();
                return new LoginResult
                {
                    Success = false,
                    ErrorMessage = "Invalid username or password."
                };
            }

            _currentUser = user;
            ResetThrottle();

            return new LoginResult
            {
                Success = true,
                User = user
            };
        }

        public void Logout()
        {
            _currentUser = null;
        }

        public static string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 10);
        }

        public static bool VerifyPassword(string password, string hash)
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, hash);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void IncrementFailedAttempts()
        {
            int count = FailCount + 1;
            FailCount = count; // cumulative — never reset on lockout, so failures escalate

            if (count >= LockoutThreshold)
            {
                int secs = LockoutSecondsFor(count);
                _config.Set(CfgLockoutUntil,
                    _now().AddSeconds(secs).ToString("o", CultureInfo.InvariantCulture));
                _monoLockoutDeadlineMs = _monotonicMs() + secs * 1000L;
            }
        }

        private void ResetThrottle()
        {
            _config.Set(CfgFailCount, "0");
            _config.Set(CfgLockoutUntil, "");
            _monoLockoutDeadlineMs = 0;
        }
    }

    public class LoginResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public User User { get; set; }
    }
}
