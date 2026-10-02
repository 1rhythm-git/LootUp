using System;
using System.Threading.Tasks;
using BackEnd;
using LitJson;
using UnityEngine;
using BackndApi = BackEnd.Backend;

namespace LootUp.Core.Backend
{
    public enum AppVersionGateStatus
    {
        Allowed,
        OptionalUpdate,
        RequiredUpdate,
        CheckFailed
    }

    public readonly struct AppVersionGateResult
    {
        public AppVersionGateResult(
            AppVersionGateStatus status,
            string currentVersion,
            string latestVersion,
            string message)
        {
            Status = status;
            CurrentVersion = currentVersion ?? string.Empty;
            LatestVersion = latestVersion ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public AppVersionGateStatus Status { get; }
        public string CurrentVersion { get; }
        public string LatestVersion { get; }
        public string Message { get; }
        public bool CanAccessServer =>
            Status == AppVersionGateStatus.Allowed
            || Status == AppVersionGateStatus.OptionalUpdate;
    }

    public static class AppVersionGate
    {
        private const int ForceUpdateType = 1;
        private const int OptionalUpdateType = 2;

        public static bool IsServerAccessAllowed { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            IsServerAccessAllowed = false;
        }

        public static Task<AppVersionGateResult> CheckAsync()
        {
            IsServerAccessAllowed = false;
            string currentVersion = Application.version;

#if UNITY_EDITOR || !UNITY_ANDROID
            AppVersionGateResult bypassResult = new(
                AppVersionGateStatus.Allowed,
                currentVersion,
                currentVersion,
                "Version gate is bypassed outside an Android player.");
            IsServerAccessAllowed = true;
            return Task.FromResult(bypassResult);
#else
            return CheckAndroidAsync(currentVersion);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static async Task<AppVersionGateResult> CheckAndroidAsync(
            string currentVersion)
        {
            try
            {
                BackndInitializationResult initializationResult =
                    await BackndSdkManager.InitializeAsync();
                if (!initializationResult.Succeeded)
                {
                    return CheckFailed(
                        currentVersion,
                        initializationResult.Message);
                }

                BackendReturnObject response = await GetLatestVersionAsync();
                if (!response.IsSuccess())
                {
                    return CheckFailed(
                        currentVersion,
                        response.GetMessage());
                }

                JsonData payload = response.GetReturnValuetoJSON();
                string latestVersion = ReadString(payload, "version");
                int updateType = ReadInt(payload, "type");
                if (string.IsNullOrWhiteSpace(latestVersion))
                {
                    return CheckFailed(
                        currentVersion,
                        "Latest version was not returned.");
                }

                bool isOlder = IsCurrentVersionOlder(
                    currentVersion,
                    latestVersion);
                AppVersionGateStatus status = ResolveStatus(
                    isOlder,
                    updateType);
                AppVersionGateResult result = new(
                    status,
                    currentVersion,
                    latestVersion,
                    string.Empty);
                IsServerAccessAllowed = result.CanAccessServer;
                return result;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return CheckFailed(currentVersion, exception.Message);
            }
        }
#endif

        private static Task<BackendReturnObject> GetLatestVersionAsync()
        {
            TaskCompletionSource<BackendReturnObject> source = new();
            try
            {
                BackndApi.Utils.GetLatestVersion(
                    VersionInfo.GooglePlayStore,
                    response => BackndSdkManager.PostToMainThread(
                        () => source.TrySetResult(response)));
            }
            catch (Exception exception)
            {
                source.TrySetException(exception);
            }

            return source.Task;
        }

        private static AppVersionGateResult CheckFailed(
            string currentVersion,
            string message)
        {
            return new AppVersionGateResult(
                AppVersionGateStatus.CheckFailed,
                currentVersion,
                string.Empty,
                message);
        }

        private static AppVersionGateStatus ResolveStatus(
            bool isOlder,
            int updateType)
        {
            if (!isOlder)
            {
                return AppVersionGateStatus.Allowed;
            }

            if (updateType == ForceUpdateType)
            {
                return AppVersionGateStatus.RequiredUpdate;
            }

            return updateType == OptionalUpdateType
                ? AppVersionGateStatus.OptionalUpdate
                : AppVersionGateStatus.RequiredUpdate;
        }

        private static bool IsCurrentVersionOlder(
            string currentVersion,
            string latestVersion)
        {
            if (Version.TryParse(currentVersion, out Version current)
                && Version.TryParse(latestVersion, out Version latest))
            {
                return current.CompareTo(latest) < 0;
            }

            return !string.Equals(
                currentVersion?.Trim(),
                latestVersion?.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadString(JsonData payload, string key)
        {
            if (payload == null
                || !payload.IsObject
                || !payload.Keys.Contains(key))
            {
                return string.Empty;
            }

            return payload[key]?.ToString() ?? string.Empty;
        }

        private static int ReadInt(JsonData payload, string key)
        {
            string value = ReadString(payload, key);
            return int.TryParse(value, out int parsed) ? parsed : 0;
        }
    }
}
