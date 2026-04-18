using System;
using Microsoft.Data.Sqlite;
using Kasir.Data.Repositories;
using Kasir.Models;

namespace Kasir.Auth
{
    public class AuthService
    {
        private readonly UserRepository _userRepo;
        private User _currentUser;
        private int _failedAttempts;
        private DateTime _lockoutUntil;

        public AuthService(SqliteConnection db)
        {
            _userRepo = new UserRepository(db);
            _failedAttempts = 0;
            _lockoutUntil = DateTime.MinValue;
        }

        public User CurrentUser
        {
            get { return _currentUser; }
        }

        public bool IsLoggedIn
        {
            get { return _currentUser != null; }
        }

        public bool IsLockedOut
        {
            get { return DateTime.Now < _lockoutUntil; }
        }

        public int RemainingLockoutSeconds
        {
            get
            {
                if (!IsLockedOut) return 0;
                return (int)(_lockoutUntil - DateTime.Now).TotalSeconds;
            }
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
            _failedAttempts = 0;

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
            _failedAttempts++;
            if (_failedAttempts >= 3)
            {
                _lockoutUntil = DateTime.Now.AddSeconds(30);
                _failedAttempts = 0;
            }
        }
    }

    public class LoginResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public User User { get; set; }
    }
}
