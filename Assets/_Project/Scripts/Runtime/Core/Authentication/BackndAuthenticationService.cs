using System;
using System.Threading.Tasks;
using BackEnd;
using LitJson;
using LootUp.Core.Backend;
using BackndApi = BackEnd.Backend;

namespace LootUp.Core.Authentication
{
    public sealed class BackndAuthenticationService : IAuthenticationService
    {
        private const int AccountIdMinimumLength = 4;
        private const int AccountIdMaximumLength = 20;
        private const int NicknameMinimumLength = 2;
        private const int NicknameMaximumLength = 12;
        private const int PasswordMinimumLength = 6;
        private const int PasswordMaximumLength = 32;

        public async Task<AuthenticationResult> TryRestoreSessionAsync()
        {
            LocalLoginCredentialPreferences.ApplyLoginFlowMigration();
            AuthenticationResult initializationFailure =
                await EnsureInitializedAsync();
            if (!initializationFailure.Succeeded)
            {
                return initializationFailure;
            }

            if (!string.IsNullOrWhiteSpace(BackndApi.BMember.GetAccessToken()))
            {
                await LogoutIgnoringFailureAsync();
            }

            return AuthenticationResult.Fail(
                AuthenticationFailure.NoSavedSession,
                "Account ID and password confirmation is required.");
        }

        public Task<NicknameAvailabilityResult>
            CheckNicknameAvailabilityAsync(string nickname)
        {
            if (!TryNormalizeNickname(
                nickname,
                out _,
                out string validationMessage))
            {
                return Task.FromResult(
                    NicknameAvailabilityResult.Unavailable(
                        AuthenticationFailure.InvalidNickname,
                        validationMessage));
            }

            return Task.FromResult(NicknameAvailabilityResult.Available());
        }

        public async Task<AuthenticationResult> RegisterAsync(
            string accountId,
            string password,
            string nickname)
        {
            if (!TryValidateRegistration(
                accountId,
                password,
                nickname,
                out string normalizedAccountId,
                out string normalizedNickname,
                out AuthenticationResult validationFailure))
            {
                return validationFailure;
            }

            AuthenticationResult initializationFailure =
                await EnsureInitializedAsync();
            if (!initializationFailure.Succeeded)
            {
                return initializationFailure;
            }

            BackendReturnObject signUpResponse = await RunRequest(
                callback => BackndApi.BMember.CustomSignUp(
                    normalizedAccountId,
                    password,
                    callback));
            bool isRecoveryAttempt = !signUpResponse.IsSuccess()
                                     && ResolveSignUpFailure(signUpResponse)
                                     == AuthenticationFailure.AccountAlreadyExists;
            if (!signUpResponse.IsSuccess())
            {
                if (!isRecoveryAttempt)
                {
                    return CreateFailure(
                        signUpResponse,
                        ResolveSignUpFailure(signUpResponse),
                        "Account could not be created.");
                }
            }

            BackendReturnObject loginResponse = await RunRequest(
                callback => BackndApi.BMember.CustomLogin(
                    normalizedAccountId,
                    password,
                    callback));
            if (!loginResponse.IsSuccess())
            {
                return CreateFailure(
                    loginResponse,
                    isRecoveryAttempt
                        ? AuthenticationFailure.AccountAlreadyExists
                        : AuthenticationFailure.Unexpected,
                    isRecoveryAttempt
                        ? "Account ID already exists or password is invalid."
                        : "Account was created, but login failed.");
            }

            if (isRecoveryAttempt)
            {
                BackendReturnObject userInfoResponse = await RunRequest(
                    callback => BackndApi.BMember.GetUserInfo(callback));
                if (!userInfoResponse.IsSuccess())
                {
                    return CreateFailure(
                        userInfoResponse,
                        AuthenticationFailure.Unexpected,
                        "Existing account information could not be loaded.");
                }

                string existingNickname = FindFirstString(
                    userInfoResponse.GetReturnValuetoJSON(),
                    "nickname");
                if (!string.IsNullOrWhiteSpace(existingNickname))
                {
                    return CreateSessionResult(
                        userInfoResponse,
                        normalizedAccountId);
                }
            }

            BackendReturnObject nicknameCheckResponse = await RunRequest(
                callback => BackndApi.BMember.CheckNicknameDuplication(
                    normalizedNickname,
                    callback));
            if (!nicknameCheckResponse.IsSuccess())
            {
                await LogoutIgnoringFailureAsync();
                return CreateFailure(
                    nicknameCheckResponse,
                    ResolveNicknameFailure(nicknameCheckResponse),
                    "Nickname is unavailable.");
            }

            BackendReturnObject nicknameResponse = await RunRequest(
                callback => BackndApi.BMember.CreateNickname(
                    normalizedNickname,
                    callback));
            if (!nicknameResponse.IsSuccess())
            {
                await LogoutIgnoringFailureAsync();
                return CreateFailure(
                    nicknameResponse,
                    ResolveNicknameFailure(nicknameResponse),
                    "Account was created, but nickname registration failed.");
            }

            return await LoadSessionAsync(normalizedAccountId);
        }

        public Task<AuthenticationResult> RegisterGuestAsync(
            string nickname,
            string password)
        {
            return RegisterAsync(nickname, password, nickname);
        }

        public Task<AuthenticationResult> SignInGuestAsync(
            string nickname,
            string password)
        {
            return SignInAsync(nickname, password);
        }

        public Task<AuthenticationResult> SignInAsGuestAsync()
        {
            return Task.FromResult(
                AuthenticationResult.Fail(
                    AuthenticationFailure.NoSavedSession,
                    "BackND Guest login is not enabled."));
        }

        public async Task<AuthenticationResult> SignInAsync(
            string accountId,
            string password)
        {
            if (!TryNormalizeAccountId(
                accountId,
                out string normalizedAccountId,
                out string accountIdMessage))
            {
                return AuthenticationResult.Fail(
                    AuthenticationFailure.InvalidAccountId,
                    accountIdMessage);
            }

            if (!TryValidatePassword(password, out string passwordMessage))
            {
                return AuthenticationResult.Fail(
                    AuthenticationFailure.WeakPassword,
                    passwordMessage);
            }

            AuthenticationResult initializationFailure =
                await EnsureInitializedAsync();
            if (!initializationFailure.Succeeded)
            {
                return initializationFailure;
            }

            BackendReturnObject response = await RunRequest(
                callback => BackndApi.BMember.CustomLogin(
                    normalizedAccountId,
                    password,
                    callback));
            if (!response.IsSuccess())
            {
                return CreateFailure(
                    response,
                    AuthenticationFailure.InvalidCredentials,
                    "Account ID or password is invalid.");
            }

            return await LoadSessionAsync(normalizedAccountId);
        }

        public async Task SignOutAsync()
        {
            if (BackndSdkManager.State != BackndInitializationState.Initialized)
            {
                return;
            }

            await RunRequest(callback => BackndApi.BMember.Logout(callback));
        }

        private static async Task<AuthenticationResult> EnsureInitializedAsync()
        {
            BackndInitializationResult result =
                await BackndSdkManager.InitializeAsync();
            return result.Succeeded
                ? AuthenticationResult.Success(
                    new AuthenticationSession(
                        "initialization",
                        "Player",
                        AuthenticationProvider.Backnd,
                        false))
                : AuthenticationResult.Fail(
                    AuthenticationFailure.ProviderUnavailable,
                    result.Message);
        }

        private static async Task<AuthenticationResult> LoadSessionAsync(
            string fallbackAccountId)
        {
            BackendReturnObject response = await RunRequest(
                callback => BackndApi.BMember.GetUserInfo(callback));
            if (!response.IsSuccess())
            {
                return CreateFailure(
                    response,
                    AuthenticationFailure.Unexpected,
                    "User information could not be loaded.");
            }

            return CreateSessionResult(response, fallbackAccountId);
        }

        private static AuthenticationResult CreateSessionResult(
            BackendReturnObject response,
            string fallbackAccountId)
        {
            JsonData userInfo = response.GetReturnValuetoJSON();
            string userId = FindFirstString(
                userInfo,
                "gamerInDate",
                "inDate",
                "gamer_id");
            string nickname = FindFirstString(userInfo, "nickname");
            string accountId = FindFirstString(
                userInfo,
                "customId",
                "customID");

            if (string.IsNullOrWhiteSpace(accountId))
            {
                accountId = fallbackAccountId;
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                return AuthenticationResult.Fail(
                    AuthenticationFailure.Unexpected,
                    "BackND user identifier was not returned.");
            }

            return AuthenticationResult.Success(
                new AuthenticationSession(
                    userId,
                    nickname,
                    AuthenticationProvider.Backnd,
                    false,
                    accountId));
        }

        private static Task<BackendReturnObject> RunRequest(
            Action<BackndApi.BackendCallback> request)
        {
            TaskCompletionSource<BackendReturnObject> source = new();
            try
            {
                request(response =>
                    BackndSdkManager.PostToMainThread(
                        () => source.TrySetResult(response)));
            }
            catch (Exception exception)
            {
                source.TrySetException(exception);
            }

            return source.Task;
        }

        private static async Task LogoutIgnoringFailureAsync()
        {
            try
            {
                await RunRequest(callback => BackndApi.BMember.Logout(callback));
            }
            catch
            {
            }
        }

        private static AuthenticationResult CreateFailure(
            BackendReturnObject response,
            AuthenticationFailure fallbackFailure,
            string fallbackMessage)
        {
            AuthenticationFailure failure = IsNetworkFailure(response)
                ? AuthenticationFailure.NetworkUnavailable
                : fallbackFailure;
            return AuthenticationResult.Fail(
                failure,
                GetSafeMessage(response, fallbackMessage));
        }

        private static AuthenticationFailure ResolveSignUpFailure(
            BackendReturnObject response)
        {
            string errorText = GetErrorText(response);
            if (errorText.Contains("duplicate")
                || errorText.Contains("already"))
            {
                return AuthenticationFailure.AccountAlreadyExists;
            }

            if (errorText.Contains("password"))
            {
                return AuthenticationFailure.WeakPassword;
            }

            if (errorText.Contains("customid")
                || errorText.Contains("custom id"))
            {
                return AuthenticationFailure.InvalidAccountId;
            }

            return AuthenticationFailure.Unexpected;
        }

        private static AuthenticationFailure ResolveNicknameFailure(
            BackendReturnObject response)
        {
            string errorText = GetErrorText(response);
            if (errorText.Contains("duplicate")
                || errorText.Contains("already"))
            {
                return AuthenticationFailure.NicknameAlreadyExists;
            }

            return errorText.Contains("nickname")
                ? AuthenticationFailure.InvalidNickname
                : AuthenticationFailure.Unexpected;
        }

        private static string GetErrorText(BackendReturnObject response)
        {
            return string.Concat(
                    response.GetErrorCode(),
                    " ",
                    response.GetMessage(),
                    " ",
                    response.GetErrorMessage())
                .ToLowerInvariant();
        }

        private static bool IsNetworkFailure(BackendReturnObject response)
        {
            return response.IsClientRequestFailError()
                   || response.IsServerError()
                   || response.IsTooManyRequestError()
                   || response.IsTooManyRequestByLocalError();
        }

        private static string GetSafeMessage(
            BackendReturnObject response,
            string fallbackMessage)
        {
            string message = response.GetMessage();
            return string.IsNullOrWhiteSpace(message)
                ? fallbackMessage
                : message;
        }

        private static bool TryValidateRegistration(
            string accountId,
            string password,
            string nickname,
            out string normalizedAccountId,
            out string normalizedNickname,
            out AuthenticationResult failure)
        {
            if (!TryNormalizeAccountId(
                accountId,
                out normalizedAccountId,
                out string accountIdMessage))
            {
                normalizedNickname = string.Empty;
                failure = AuthenticationResult.Fail(
                    AuthenticationFailure.InvalidAccountId,
                    accountIdMessage);
                return false;
            }

            if (!TryValidatePassword(password, out string passwordMessage))
            {
                normalizedNickname = string.Empty;
                failure = AuthenticationResult.Fail(
                    AuthenticationFailure.WeakPassword,
                    passwordMessage);
                return false;
            }

            if (!TryNormalizeNickname(
                nickname,
                out normalizedNickname,
                out string nicknameMessage))
            {
                failure = AuthenticationResult.Fail(
                    AuthenticationFailure.InvalidNickname,
                    nicknameMessage);
                return false;
            }

            failure = default;
            return true;
        }

        private static bool TryNormalizeAccountId(
            string accountId,
            out string normalizedAccountId,
            out string message)
        {
            normalizedAccountId = string.IsNullOrWhiteSpace(accountId)
                ? string.Empty
                : accountId.Trim().ToLowerInvariant();
            if (normalizedAccountId.Length < AccountIdMinimumLength
                || normalizedAccountId.Length > AccountIdMaximumLength)
            {
                message =
                    $"Account ID must be {AccountIdMinimumLength}-{AccountIdMaximumLength} characters.";
                return false;
            }

            for (int i = 0; i < normalizedAccountId.Length; i++)
            {
                char character = normalizedAccountId[i];
                bool isAsciiLetterOrDigit =
                    character >= 'a' && character <= 'z'
                    || character >= '0' && character <= '9';
                if (!isAsciiLetterOrDigit
                    && character != '_'
                    && character != '-')
                {
                    message =
                        "Account ID can use English letters, numbers, underscore, and hyphen.";
                    return false;
                }
            }

            message = string.Empty;
            return true;
        }

        private static bool TryNormalizeNickname(
            string nickname,
            out string normalizedNickname,
            out string message)
        {
            normalizedNickname = string.IsNullOrWhiteSpace(nickname)
                ? string.Empty
                : nickname.Trim().Normalize();
            if (normalizedNickname.Length < NicknameMinimumLength
                || normalizedNickname.Length > NicknameMaximumLength)
            {
                message =
                    $"Nickname must be {NicknameMinimumLength}-{NicknameMaximumLength} characters.";
                return false;
            }

            for (int i = 0; i < normalizedNickname.Length; i++)
            {
                char character = normalizedNickname[i];
                if (!char.IsLetterOrDigit(character) && character != '_')
                {
                    message =
                        "Nickname can use letters, numbers, and underscore.";
                    return false;
                }
            }

            message = string.Empty;
            return true;
        }

        private static bool TryValidatePassword(
            string password,
            out string message)
        {
            if (string.IsNullOrEmpty(password)
                || password.Length < PasswordMinimumLength
                || password.Length > PasswordMaximumLength)
            {
                message =
                    $"Password must be {PasswordMinimumLength}-{PasswordMaximumLength} characters.";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private static string FindFirstString(
            JsonData node,
            params string[] keys)
        {
            if (node == null)
            {
                return string.Empty;
            }

            if (node.IsObject)
            {
                foreach (string key in keys)
                {
                    if (node.Keys.Contains(key))
                    {
                        JsonData value = node[key];
                        if (value != null
                            && !value.IsObject
                            && !value.IsArray)
                        {
                            return value.ToString();
                        }
                    }
                }

                foreach (string childKey in node.Keys)
                {
                    string result = FindFirstString(node[childKey], keys);
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        return result;
                    }
                }
            }
            else if (node.IsArray)
            {
                for (int i = 0; i < node.Count; i++)
                {
                    string result = FindFirstString(node[i], keys);
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        return result;
                    }
                }
            }

            return string.Empty;
        }
    }
}
