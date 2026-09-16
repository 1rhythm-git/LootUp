using System;
using System.Threading.Tasks;
using LootUp.Core.Characters;
using LootUp.Core.Currency;
using LootUp.Core.Items;
using LootUp.Core.Leaderboard;
using LootUp.Core.Profile;

namespace LootUp.Core.Authentication
{
    public static class AuthenticationManager
    {
        private static IAuthenticationService service;
        private static bool isOperationInProgress;

        public static event Action<AuthenticationState> AuthenticationStateChanged;

        public static IAuthenticationService Service =>
            service ??= CreateDefaultService();
        public static AuthenticationState State { get; private set; } =
            AuthenticationState.SignedOut;
        public static AuthenticationSession CurrentSession { get; private set; }
        public static bool IsAuthenticated =>
            State == AuthenticationState.Authenticated
            && CurrentSession != null;

        public static void Configure(
            IAuthenticationService authenticationService)
        {
            service = authenticationService ?? CreateDefaultService();
            CurrentSession = null;
            ResetSessionServices();
            SetState(AuthenticationState.SignedOut);
        }

        public static async Task<AuthenticationResult> InitializeAsync(
            bool allowGuestFallback = false,
            bool forceSessionValidation = false)
        {
            if (IsAuthenticated && !forceSessionValidation)
            {
                return AuthenticationResult.Success(CurrentSession);
            }

            return await RunAuthenticationOperationAsync(async () =>
            {
                AuthenticationResult result =
                    await Service.TryRestoreSessionAsync();
                if (!result.Succeeded
                    && allowGuestFallback
                    && result.Failure == AuthenticationFailure.NoSavedSession)
                {
                    result = await Service.SignInAsGuestAsync();
                }

                return result;
            });
        }

        public static Task<NicknameAvailabilityResult>
            CheckGuestNicknameAvailabilityAsync(string nickname)
        {
            return Service.CheckNicknameAvailabilityAsync(nickname);
        }

        public static Task<AuthenticationResult> RegisterGuestAsync(
            string nickname,
            string password)
        {
            return RunAuthenticationOperationAsync(
                () => Service.RegisterGuestAsync(nickname, password),
                true);
        }

        public static Task<AuthenticationResult> RegisterAsync(
            string accountId,
            string password,
            string nickname)
        {
            return RunAuthenticationOperationAsync(
                () => Service.RegisterAsync(
                    accountId,
                    password,
                    nickname),
                true);
        }

        public static Task<AuthenticationResult> SignInGuestAsync(
            string nickname,
            string password)
        {
            return RunAuthenticationOperationAsync(
                () => Service.SignInGuestAsync(nickname, password));
        }

        public static Task<AuthenticationResult> SignInAsGuestAsync()
        {
            return RunAuthenticationOperationAsync(
                () => Service.SignInAsGuestAsync());
        }

        public static Task<AuthenticationResult> SignInAsync(
            string accountId,
            string password)
        {
            return RunAuthenticationOperationAsync(
                () => Service.SignInAsync(accountId, password));
        }

        public static void RequireCredentialConfirmation()
        {
            CurrentSession = null;
            ResetSessionServices();
            SetState(AuthenticationState.SignedOut);
        }

        public static async Task SignOutAsync()
        {
            if (isOperationInProgress)
            {
                return;
            }

            isOperationInProgress = true;
            try
            {
                await Service.SignOutAsync();
                CurrentSession = null;
                ResetSessionServices();
                SetState(AuthenticationState.SignedOut);
            }
            catch
            {
                CurrentSession = null;
                ResetSessionServices();
                SetState(AuthenticationState.Failed);
            }
            finally
            {
                isOperationInProgress = false;
            }
        }

        private static async Task<AuthenticationResult>
            RunAuthenticationOperationAsync(
                Func<Task<AuthenticationResult>> operation,
                bool resetEditorPlayerDataOnSuccess = false)
        {
            if (isOperationInProgress)
            {
                return AuthenticationResult.Fail(
                    AuthenticationFailure.OperationInProgress,
                    "Another authentication operation is already running.");
            }

            isOperationInProgress = true;
            SetState(AuthenticationState.Authenticating);

            AuthenticationResult result;
            try
            {
                result = await operation();
            }
            catch (Exception exception)
            {
                result = AuthenticationResult.Fail(
                    AuthenticationFailure.Unexpected,
                    exception.Message);
            }
            finally
            {
                isOperationInProgress = false;
            }

            if (result.Succeeded)
            {
                if (resetEditorPlayerDataOnSuccess)
                {
                    EditorGuestDataResetter.ResetPlayerData();
                }

                CurrentSession = result.Session;
                LocalCharacterProgressionService characterProgression =
                    new LocalCharacterProgressionService(
                        result.Session.UserId);
                CharacterProgressionState.Configure(characterProgression);
                CharacterProgressionManager.Configure(
                    result.Session.Provider == AuthenticationProvider.Backnd
                        ? new BackndCharacterProgressionService(
                            result.Session.UserId)
                        : null,
                    characterProgression,
                    result.Session.UserId);
                CharacterProgressionSynchronizationResult characterSync =
                    await CharacterProgressionManager.InitializeAsync();
                if (result.Session.Provider == AuthenticationProvider.Backnd
                    && !characterSync.Succeeded)
                {
                    try
                    {
                        await Service.SignOutAsync();
                    }
                    catch
                    {
                        // 인증 실패 상태 전환을 우선하며 서버 로그아웃 오류는 무시한다.
                    }

                    CurrentSession = null;
                    ResetSessionServices();
                    CharacterProgressionManager.MarkSynchronizationFailed(
                        characterSync.Message);
                    SetState(AuthenticationState.Failed);
                    return AuthenticationResult.Fail(
                        AuthenticationFailure.Unexpected,
                        string.IsNullOrWhiteSpace(characterSync.Message)
                            ? "Character data synchronization failed."
                            : characterSync.Message);
                }

                CharacterSelectionState.Reset();
                UserProfileManager.Configure(
                    new LocalUserProfileService(
                        result.Session.UserId));
                UserProfileManager.SetIdentity(
                    result.Session.UserId,
                    result.Session.Nickname);
                CurrencyLedgerManager.Configure(
                    result.Session.Provider == AuthenticationProvider.Backnd
                        ? new BackndCurrencyLedgerService(
                            result.Session.UserId)
                        : null,
                    result.Session.UserId);
                await CurrencyLedgerManager.InitializeAsync();
                ItemCollectionManager.Configure(
                    new LocalCollectionInventoryService(
                        result.Session.UserId));
                LeaderboardManager.Configure(
                    result.Session.Provider == AuthenticationProvider.Backnd
                        ? new BackndLeaderboardService(
                            result.Session.UserId)
                        : null);
                await LeaderboardManager
                    .SynchronizeLifetimeBestAsync();
                SetState(AuthenticationState.Authenticated);
            }
            else
            {
                CurrentSession = null;
                ResetSessionServices();
                SetState(AuthenticationState.Failed);
            }

            return result;
        }

        private static IAuthenticationService CreateDefaultService()
        {
            return new BackndAuthenticationService();
        }

        private static void ResetSessionServices()
        {
            LeaderboardManager.Configure(null);
            CurrencyLedgerManager.Configure(null, string.Empty);
            LocalCharacterProgressionService characterProgression =
                new LocalCharacterProgressionService();
            CharacterProgressionState.Configure(characterProgression);
            CharacterProgressionManager.Configure(
                null,
                characterProgression,
                string.Empty);
            CharacterSelectionState.Reset();
            ItemCollectionManager.Configure(
                new LocalCollectionInventoryService());
        }

        private static void SetState(AuthenticationState state)
        {
            State = state;
            AuthenticationStateChanged?.Invoke(state);
        }
    }
}
