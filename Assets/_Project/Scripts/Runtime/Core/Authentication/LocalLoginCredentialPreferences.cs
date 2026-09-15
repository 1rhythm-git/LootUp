using System;
using System.Text;
using UnityEngine;

namespace LootUp.Core.Authentication
{
    public static class LocalLoginCredentialPreferences
    {
        private const string LoginFlowMigrationKey =
            "LootUp.Login.FlowMigration.v4";
        private const string RememberAccountKey =
            "LootUp.Login.RememberAccount.v4";
        private const string AccountIdKey = "LootUp.Login.AccountId.v4";
        private const string PasswordKey = "LootUp.Login.Password.v4";

        private static readonly string[] LegacyCredentialKeys =
        {
            "LootUp.Login.RememberCredentials.v1",
            "LootUp.Login.Nickname.v1",
            "LootUp.Login.Password.v1",
            "LootUp.Login.AutoLogin.v2",
            "LootUp.Login.AccountId.v2",
            "LootUp.Login.RememberCredentials.v3",
            "LootUp.Login.AccountId.v3",
            "LootUp.Login.Password.v3"
        };

        private static readonly string[] LegacyLocalAccountKeys =
        {
            "LootUp.Authentication.v2",
            "LootUp.Authentication.v1",
            "PH.Authentication.v1",
            "LootUp.GuestAccounts.v1"
        };

        public static bool ApplyLoginFlowMigration()
        {
            if (PlayerPrefs.GetInt(LoginFlowMigrationKey, 0) == 1)
            {
                DeleteLegacyCredentials();
                return false;
            }

            DeleteLegacyCredentials();
            DeleteKeys(LegacyLocalAccountKeys);
            PlayerPrefs.DeleteKey(RememberAccountKey);
            PlayerPrefs.DeleteKey(AccountIdKey);
            PlayerPrefs.DeleteKey(PasswordKey);
            PlayerPrefs.SetInt(LoginFlowMigrationKey, 1);
            PlayerPrefs.Save();
            return true;
        }

        public static bool TryLoad(out string accountId, out string password)
        {
            accountId = string.Empty;
            password = string.Empty;
            if (PlayerPrefs.GetInt(RememberAccountKey, 0) != 1)
            {
                return false;
            }

            accountId = PlayerPrefs.GetString(AccountIdKey, string.Empty);
            string encodedPassword =
                PlayerPrefs.GetString(PasswordKey, string.Empty);
            try
            {
                password = Encoding.UTF8.GetString(
                    Convert.FromBase64String(encodedPassword));
                return !string.IsNullOrWhiteSpace(accountId)
                       && !string.IsNullOrEmpty(password);
            }
            catch (FormatException)
            {
                Clear();
                accountId = string.Empty;
                password = string.Empty;
                return false;
            }
        }

        public static void Save(string accountId, string password)
        {
            DeleteLegacyCredentials();
            PlayerPrefs.SetInt(RememberAccountKey, 1);
            PlayerPrefs.SetString(
                AccountIdKey,
                accountId?.Trim() ?? string.Empty);
            PlayerPrefs.SetString(
                PasswordKey,
                Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(password ?? string.Empty)));
            PlayerPrefs.Save();
        }

        public static void Clear()
        {
            DeleteLegacyCredentials();
            PlayerPrefs.DeleteKey(RememberAccountKey);
            PlayerPrefs.DeleteKey(AccountIdKey);
            PlayerPrefs.DeleteKey(PasswordKey);
            PlayerPrefs.Save();
        }

        public static void DeleteLegacyCredentials()
        {
            if (DeleteKeys(LegacyCredentialKeys))
            {
                PlayerPrefs.Save();
            }
        }

        private static bool DeleteKeys(string[] keys)
        {
            bool deletedAny = false;
            for (int i = 0; i < keys.Length; i++)
            {
                if (PlayerPrefs.HasKey(keys[i]))
                {
                    deletedAny = true;
                }

                PlayerPrefs.DeleteKey(keys[i]);
            }

            return deletedAny;
        }
    }
}
