using System;
using System.Collections;
using System.Threading.Tasks;
using LootUp.Core.Authentication;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LootUp.Core.SceneFlow
{
    public sealed class TitleSceneController : MonoBehaviour
    {
        private const string RuntimeRootName = "TitleRuntimeRoot";
        private const int AccountIdMinimumLength = 4;
        private const int AccountIdMaximumLength = 20;
        private const int PasswordMinimumLength = 6;
        private const int PasswordMaximumLength = 32;

        [SerializeField]
        private Sprite backgroundSprite;

        [SerializeField, Min(0f)]
        private float minimumLoadingDuration = 1.5f;

        [SerializeField, Min(0.05f)]
        private float touchBlinkInterval = 0.45f;

        [SerializeField]
        private Color loadingBarBackgroundColor =
            new Color(0f, 0f, 0f, 0.72f);

        [SerializeField]
        private Color loadingBarFillColor =
            new Color(0.96f, 0.82f, 0.22f, 1f);

        private RectTransform loadingBarFillRect;
        private Text statusText;
        private RectTransform loginPanelRoot;
        private RectTransform guestLoginRoot;
        private RectTransform nicknameSetupRoot;
        private RectTransform googleLoginRoot;
        private Text accountFlowTitleText;
        private Text loginMessageText;
        private InputField accountIdInput;
        private InputField nicknameInput;
        private InputField passwordInput;
        private Toggle rememberCredentialsToggle;
        private Button googleSignupButton;
        private Button backToGuestButton;
        private Button nicknameCheckButton;
        private Button guestRegisterButton;
        private Button guestLoginButton;
        private Button registrationNextButton;
        private Button showRegistrationButton;
        private Button backToLoginButton;
        private Button backToRegistrationButton;
        private AsyncOperation lobbyLoadOperation;
        private bool isReadyForTouch;
        private bool isLoginPanelVisible;
        private bool isAuthenticationOperationActive;
        private bool sceneActivationRequested;
        private float blinkElapsed;
        private string verifiedNickname = string.Empty;
        private string pendingRegistrationAccountId = string.Empty;
        private string pendingRegistrationPassword = string.Empty;

        private IEnumerator Start()
        {
            BuildTitleUI();
            EnsureEventSystem();

            Task<AuthenticationResult> authenticationTask =
                AuthenticationManager.InitializeAsync(false, true);

            lobbyLoadOperation = SceneManager.LoadSceneAsync(
                SceneFlowManager.LobbySceneName,
                LoadSceneMode.Single);
            if (lobbyLoadOperation != null)
            {
                lobbyLoadOperation.allowSceneActivation = false;
            }

            float elapsed = 0f;
            while (!IsLobbyReady()
                   || elapsed < minimumLoadingDuration
                   || !authenticationTask.IsCompleted)
            {
                elapsed += Time.unscaledDeltaTime;
                float loadProgress = lobbyLoadOperation != null
                    ? Mathf.Clamp01(lobbyLoadOperation.progress / 0.9f)
                    : 1f;
                float minimumDurationProgress = minimumLoadingDuration > 0f
                    ? Mathf.Clamp01(elapsed / minimumLoadingDuration)
                    : 1f;
                SetLoadingProgress(
                    Mathf.Min(loadProgress, minimumDurationProgress));

                if (IsLobbyReady()
                    && elapsed >= minimumLoadingDuration
                    && !authenticationTask.IsCompleted)
                {
                    statusText.text = "SIGNING IN";
                }

                yield return null;
            }

            SetLoadingProgress(1f);
            ApplyInitialAuthenticationResult(authenticationTask.Result);
        }

        private void Update()
        {
            if (isLoginPanelVisible
                || !isReadyForTouch
                || sceneActivationRequested)
            {
                return;
            }

            blinkElapsed += Time.unscaledDeltaTime;
            bool isBright =
                Mathf.FloorToInt(blinkElapsed / touchBlinkInterval) % 2 == 0;
            Color textColor = statusText.color;
            textColor.a = isBright ? 1f : 0.25f;
            statusText.color = textColor;

            if (!WasTouchPressed())
            {
                return;
            }

            sceneActivationRequested = true;
            if (lobbyLoadOperation != null)
            {
                lobbyLoadOperation.allowSceneActivation = true;
                return;
            }

            SceneManager.LoadScene(
                SceneFlowManager.LobbySceneName,
                LoadSceneMode.Single);
        }

        private void OnGoogleSignupButtonPressed()
        {
            if (isAuthenticationOperationActive)
            {
                return;
            }

            ShowGoogleLoginView();
        }

        private void OnBackToGuestButtonPressed()
        {
            if (isAuthenticationOperationActive)
            {
                return;
            }

            ShowAccountLoginView("ENTER ID AND PASSWORD");
        }

        private void OnShowRegistrationButtonPressed()
        {
            if (isAuthenticationOperationActive)
            {
                return;
            }

            pendingRegistrationAccountId = string.Empty;
            pendingRegistrationPassword = string.Empty;
            verifiedNickname = string.Empty;
            if (accountIdInput != null)
            {
                accountIdInput.text = string.Empty;
            }

            if (passwordInput != null)
            {
                passwordInput.text = string.Empty;
            }

            ShowRegistrationCredentialsView("CREATE A LOGIN ID");
        }

        private void OnBackToLoginButtonPressed()
        {
            if (isAuthenticationOperationActive)
            {
                return;
            }

            pendingRegistrationAccountId = string.Empty;
            pendingRegistrationPassword = string.Empty;
            verifiedNickname = string.Empty;
            RestoreCredentialPreference();
            ShowAccountLoginView("ENTER ID AND PASSWORD");
        }

        private void OnRegistrationNextButtonPressed()
        {
            if (isAuthenticationOperationActive
                || !TryGetNewAccountCredentials(
                    out pendingRegistrationAccountId,
                    out pendingRegistrationPassword))
            {
                return;
            }

            verifiedNickname = string.Empty;
            if (nicknameInput != null)
            {
                nicknameInput.text = string.Empty;
            }

            ShowNicknameSetupView("CHOOSE YOUR NICKNAME");
        }

        private void OnBackToRegistrationButtonPressed()
        {
            if (isAuthenticationOperationActive)
            {
                return;
            }

            if (accountIdInput != null)
            {
                accountIdInput.text = pendingRegistrationAccountId;
            }

            if (passwordInput != null)
            {
                passwordInput.text = pendingRegistrationPassword;
            }

            ShowRegistrationCredentialsView("CHECK YOUR LOGIN ID");
        }

        private void OnNicknameChanged(string nickname)
        {
            if (!string.Equals(
                verifiedNickname,
                nickname?.Trim(),
                StringComparison.OrdinalIgnoreCase))
            {
                verifiedNickname = string.Empty;
            }
        }

        private void OnRememberCredentialsChanged(bool isEnabled)
        {
            if (!isEnabled)
            {
                LocalLoginCredentialPreferences.Clear();
            }
        }

        private void OnNicknameCheckButtonPressed()
        {
            if (isAuthenticationOperationActive)
            {
                return;
            }

            string nickname = GetNickname();
            if (string.IsNullOrWhiteSpace(nickname))
            {
                SetLoginMessage("ENTER A NICKNAME");
                return;
            }

            StartCoroutine(
                RunNicknameAvailabilityCheck(
                    AuthenticationManager
                        .CheckGuestNicknameAvailabilityAsync(nickname),
                    nickname));
        }

        private void OnGuestRegisterButtonPressed()
        {
            if (isAuthenticationOperationActive
                || !TryGetRegistrationNickname(out string nickname))
            {
                return;
            }

            if (!string.Equals(
                verifiedNickname,
                nickname,
                StringComparison.OrdinalIgnoreCase))
            {
                SetLoginMessage("CHECK NICKNAME FIRST");
                return;
            }

            StartCoroutine(
                RunAuthenticationOperation(
                    AuthenticationManager.RegisterAsync(
                        pendingRegistrationAccountId,
                        pendingRegistrationPassword,
                        nickname),
                    "CREATING ACCOUNT",
                    true));
        }

        private void OnGuestLoginButtonPressed()
        {
            if (isAuthenticationOperationActive
                || !TryGetLoginCredentials(
                    out string accountId,
                    out string password))
            {
                return;
            }

            StartCoroutine(
                RunAuthenticationOperation(
                    AuthenticationManager.SignInAsync(
                        accountId,
                        password),
                    "SIGNING IN"));
        }

        private IEnumerator RunNicknameAvailabilityCheck(
            Task<NicknameAvailabilityResult> availabilityTask,
            string checkedNickname)
        {
            isAuthenticationOperationActive = true;
            SetLoginControlsInteractable(false);
            SetLoginMessage("CHECKING NICKNAME");

            while (!availabilityTask.IsCompleted)
            {
                yield return null;
            }

            isAuthenticationOperationActive = false;
            SetLoginControlsInteractable(true);

            if (availabilityTask.IsFaulted)
            {
                verifiedNickname = string.Empty;
                SetLoginMessage("NICKNAME CHECK FAILED");
                yield break;
            }

            NicknameAvailabilityResult result = availabilityTask.Result;
            if (result.IsAvailable)
            {
                verifiedNickname = checkedNickname.Trim();
                SetLoginMessage("NICKNAME FORMAT OK");
                yield break;
            }

            verifiedNickname = string.Empty;
            SetLoginMessage(GetNicknameAvailabilityMessage(result));
        }

        private IEnumerator RunAuthenticationOperation(
            Task<AuthenticationResult> authenticationTask,
            string progressMessage,
            bool returnToNicknameSetupOnFailure = false)
        {
            isAuthenticationOperationActive = true;
            isReadyForTouch = false;
            SetLoginControlsInteractable(false);
            SetLoginMessage(progressMessage);
            statusText.text = progressMessage;
            SetStatusTextAlpha(1f);

            while (!authenticationTask.IsCompleted)
            {
                yield return null;
            }

            isAuthenticationOperationActive = false;
            ApplyLoginAuthenticationResult(
                authenticationTask.Result,
                returnToNicknameSetupOnFailure);
        }

        private void ApplyInitialAuthenticationResult(
            AuthenticationResult result)
        {
            if (result.Succeeded)
            {
                ShowReadyForTouch("TOUCH");
                return;
            }

            string message = result.Failure == AuthenticationFailure.NoSavedSession
                ? "LOGIN OR CREATE ACCOUNT"
                : GetAuthenticationFailureMessage(result);
            ShowLoginPanel(message);
        }

        private void ApplyLoginAuthenticationResult(
            AuthenticationResult result,
            bool returnToNicknameSetupOnFailure)
        {
            if (result.Succeeded)
            {
                if (returnToNicknameSetupOnFailure)
                {
                    LocalLoginCredentialPreferences.Clear();
                }
                else
                {
                    SaveCredentialPreference();
                }

                if (passwordInput != null)
                {
                    passwordInput.text = string.Empty;
                }

                ShowReadyForTouch("TOUCH");
                return;
            }

            string message = GetAuthenticationFailureMessage(result);
            if (returnToNicknameSetupOnFailure)
            {
                isLoginPanelVisible = true;
                if (loginPanelRoot != null)
                {
                    loginPanelRoot.gameObject.SetActive(true);
                }

                ShowNicknameSetupView(message);
                SetLoginControlsInteractable(true);
                return;
            }

            ShowLoginPanel(message);
        }

        private bool TryGetRegistrationNickname(out string nickname)
        {
            nickname = GetNickname();
            if (string.IsNullOrWhiteSpace(pendingRegistrationAccountId)
                || string.IsNullOrEmpty(pendingRegistrationPassword))
            {
                SetLoginMessage("ENTER ID AND PASSWORD FIRST");
                ShowRegistrationCredentialsView("ENTER ID AND PASSWORD FIRST");
                return false;
            }

            if (string.IsNullOrWhiteSpace(nickname))
            {
                SetLoginMessage("ENTER A NICKNAME");
                return false;
            }

            return true;
        }

        private bool TryGetLoginCredentials(
            out string accountId,
            out string password)
        {
            accountId = GetAccountId();
            password = passwordInput != null ? passwordInput.text : string.Empty;
            if (string.IsNullOrWhiteSpace(accountId)
                || string.IsNullOrEmpty(password))
            {
                SetLoginMessage("ENTER ID AND PASSWORD");
                return false;
            }

            return true;
        }

        private bool TryGetNewAccountCredentials(
            out string accountId,
            out string password)
        {
            if (!TryGetLoginCredentials(out accountId, out password))
            {
                return false;
            }

            if (accountId.Length < AccountIdMinimumLength
                || accountId.Length > AccountIdMaximumLength)
            {
                SetLoginMessage(
                    $"ID MUST BE {AccountIdMinimumLength}-{AccountIdMaximumLength} CHARACTERS");
                return false;
            }

            for (int i = 0; i < accountId.Length; i++)
            {
                char character = accountId[i];
                bool isAllowed = character >= 'a' && character <= 'z'
                    || character >= 'A' && character <= 'Z'
                    || character >= '0' && character <= '9'
                    || character == '_'
                    || character == '-';
                if (!isAllowed)
                {
                    SetLoginMessage("ID USES LETTERS, NUMBERS, _ OR -");
                    return false;
                }
            }

            if (password.Length < PasswordMinimumLength
                || password.Length > PasswordMaximumLength)
            {
                SetLoginMessage(
                    $"PASSWORD MUST BE {PasswordMinimumLength}-{PasswordMaximumLength} CHARACTERS");
                return false;
            }

            return true;
        }

        private string GetAccountId()
        {
            return accountIdInput != null
                ? accountIdInput.text.Trim()
                : string.Empty;
        }

        private string GetNickname()
        {
            return nicknameInput != null
                ? nicknameInput.text.Trim()
                : string.Empty;
        }

        private void SaveCredentialPreference()
        {
            if (rememberCredentialsToggle != null
                && rememberCredentialsToggle.isOn)
            {
                string accountId = !string.IsNullOrWhiteSpace(
                    pendingRegistrationAccountId)
                    ? pendingRegistrationAccountId
                    : GetAccountId();
                LocalLoginCredentialPreferences.Save(
                    accountId,
                    passwordInput != null ? passwordInput.text : string.Empty);
                return;
            }

            LocalLoginCredentialPreferences.Clear();
        }

        private void RestoreCredentialPreference()
        {
            bool hasCredentials =
                LocalLoginCredentialPreferences.TryLoad(
                    out string accountId,
                    out string password);
            if (rememberCredentialsToggle != null)
            {
                rememberCredentialsToggle.SetIsOnWithoutNotify(hasCredentials);
            }

            if (accountIdInput != null)
            {
                accountIdInput.text = hasCredentials
                    ? accountId
                    : string.Empty;
            }

            if (passwordInput != null)
            {
                passwordInput.text = hasCredentials
                    ? password
                    : string.Empty;
            }
        }

        private void ShowReadyForTouch(string message)
        {
            HideLoginPanel();
            isReadyForTouch = true;
            blinkElapsed = 0f;
            statusText.text = message;
            SetStatusTextAlpha(1f);
        }

        private void ShowLoginPanel(string message)
        {
            isReadyForTouch = false;
            isLoginPanelVisible = true;
            if (loginPanelRoot != null)
            {
                loginPanelRoot.gameObject.SetActive(true);
            }

            ShowAccountLoginView(message);
            SetLoginControlsInteractable(true);
            statusText.text = "LOGIN REQUIRED";
            SetStatusTextAlpha(1f);
        }

        private void ShowGuestLoginView(string message)
        {
            ShowAccountLoginView(message);
        }

        private void ShowAccountLoginView(string message)
        {
            if (guestLoginRoot != null)
            {
                guestLoginRoot.gameObject.SetActive(true);
            }

            if (nicknameSetupRoot != null)
            {
                nicknameSetupRoot.gameObject.SetActive(false);
            }

            if (googleLoginRoot != null)
            {
                googleLoginRoot.gameObject.SetActive(false);
            }

            if (accountFlowTitleText != null)
            {
                accountFlowTitleText.text = "ACCOUNT LOGIN";
            }

            SetAccountEntryMode(false);

            SetLoginMessage(message);
            statusText.text = "LOGIN REQUIRED";
            SetStatusTextAlpha(1f);
        }

        private void ShowRegistrationCredentialsView(string message)
        {
            if (guestLoginRoot != null)
            {
                guestLoginRoot.gameObject.SetActive(true);
            }

            if (nicknameSetupRoot != null)
            {
                nicknameSetupRoot.gameObject.SetActive(false);
            }

            if (googleLoginRoot != null)
            {
                googleLoginRoot.gameObject.SetActive(false);
            }

            if (accountFlowTitleText != null)
            {
                accountFlowTitleText.text = "CREATE ACCOUNT - 1/2";
            }

            SetAccountEntryMode(true);

            SetLoginMessage(message);
            statusText.text = "LOGIN REQUIRED";
            SetStatusTextAlpha(1f);
        }

        private void ShowNicknameSetupView(string message)
        {
            if (guestLoginRoot != null)
            {
                guestLoginRoot.gameObject.SetActive(false);
            }

            if (nicknameSetupRoot != null)
            {
                nicknameSetupRoot.gameObject.SetActive(true);
            }

            if (googleLoginRoot != null)
            {
                googleLoginRoot.gameObject.SetActive(false);
            }

            SetLoginMessage(message);
            statusText.text = "CREATE ACCOUNT";
            SetStatusTextAlpha(1f);
        }

        private void SetAccountEntryMode(bool registrationMode)
        {
            if (rememberCredentialsToggle != null)
            {
                rememberCredentialsToggle.gameObject.SetActive(
                    !registrationMode);
            }

            if (guestLoginButton != null)
            {
                guestLoginButton.gameObject.SetActive(!registrationMode);
            }

            if (showRegistrationButton != null)
            {
                showRegistrationButton.gameObject.SetActive(!registrationMode);
            }

            if (registrationNextButton != null)
            {
                registrationNextButton.gameObject.SetActive(registrationMode);
            }

            if (backToLoginButton != null)
            {
                backToLoginButton.gameObject.SetActive(registrationMode);
            }
        }

        private void ShowGoogleLoginView()
        {
            if (guestLoginRoot != null)
            {
                guestLoginRoot.gameObject.SetActive(false);
            }

            if (nicknameSetupRoot != null)
            {
                nicknameSetupRoot.gameObject.SetActive(false);
            }

            if (googleLoginRoot != null)
            {
                googleLoginRoot.gameObject.SetActive(true);
            }

            SetLoginMessage("GOOGLE SIGN UP REQUIRES BACKND");
            statusText.text = "COMING SOON";
            SetStatusTextAlpha(1f);
        }

        private void HideLoginPanel()
        {
            isLoginPanelVisible = false;
            isAuthenticationOperationActive = false;
            if (loginPanelRoot != null)
            {
                loginPanelRoot.gameObject.SetActive(false);
            }
        }

        private void SetLoginControlsInteractable(bool interactable)
        {
            if (googleSignupButton != null)
            {
                googleSignupButton.interactable = interactable;
            }

            if (backToGuestButton != null)
            {
                backToGuestButton.interactable = interactable;
            }

            if (nicknameCheckButton != null)
            {
                nicknameCheckButton.interactable = interactable;
            }

            if (guestRegisterButton != null)
            {
                guestRegisterButton.interactable = interactable;
            }

            if (guestLoginButton != null)
            {
                guestLoginButton.interactable = interactable;
            }

            if (registrationNextButton != null)
            {
                registrationNextButton.interactable = interactable;
            }

            if (showRegistrationButton != null)
            {
                showRegistrationButton.interactable = interactable;
            }

            if (backToLoginButton != null)
            {
                backToLoginButton.interactable = interactable;
            }

            if (backToRegistrationButton != null)
            {
                backToRegistrationButton.interactable = interactable;
            }

            if (nicknameInput != null)
            {
                nicknameInput.interactable = interactable;
            }

            if (accountIdInput != null)
            {
                accountIdInput.interactable = interactable;
            }

            if (passwordInput != null)
            {
                passwordInput.interactable = interactable;
            }

            if (rememberCredentialsToggle != null)
            {
                rememberCredentialsToggle.interactable = interactable;
            }

        }

        private void SetLoginMessage(string message)
        {
            if (loginMessageText != null)
            {
                loginMessageText.text = string.IsNullOrWhiteSpace(message)
                    ? "SELECT LOGIN METHOD"
                    : message;
            }
        }

        private static string GetNicknameAvailabilityMessage(
            NicknameAvailabilityResult result)
        {
            switch (result.Failure)
            {
                case AuthenticationFailure.InvalidNickname:
                    return string.IsNullOrWhiteSpace(result.Message)
                        ? "INVALID NICKNAME"
                        : result.Message.ToUpperInvariant();
                case AuthenticationFailure.NicknameAlreadyExists:
                    return "NICKNAME ALREADY EXISTS";
                case AuthenticationFailure.GuestAccountLimitReached:
                    return "ANDROID DEVICE ALREADY HAS A GUEST ACCOUNT";
                default:
                    return string.IsNullOrWhiteSpace(result.Message)
                        ? "NICKNAME CHECK FAILED"
                        : result.Message.ToUpperInvariant();
            }
        }

        private static string GetAuthenticationFailureMessage(
            AuthenticationResult result)
        {
            switch (result.Failure)
            {
                case AuthenticationFailure.InvalidCredentials:
                    return "INVALID ID OR PASSWORD";
                case AuthenticationFailure.InvalidAccountId:
                    return string.IsNullOrWhiteSpace(result.Message)
                        ? "INVALID ACCOUNT ID"
                        : result.Message.ToUpperInvariant();
                case AuthenticationFailure.AccountAlreadyExists:
                    return "ACCOUNT ID ALREADY EXISTS";
                case AuthenticationFailure.InvalidNickname:
                    return string.IsNullOrWhiteSpace(result.Message)
                        ? "INVALID NICKNAME"
                        : result.Message.ToUpperInvariant();
                case AuthenticationFailure.NicknameAlreadyExists:
                    return "NICKNAME ALREADY EXISTS";
                case AuthenticationFailure.NicknameNotFound:
                    return "NICKNAME NOT FOUND";
                case AuthenticationFailure.GuestAccountLimitReached:
                    return "ANDROID DEVICE ALREADY HAS A GUEST ACCOUNT";
                case AuthenticationFailure.WeakPassword:
                    return string.IsNullOrWhiteSpace(result.Message)
                        ? "PASSWORD MUST BE 6-32 CHARACTERS"
                        : result.Message.ToUpperInvariant();
                case AuthenticationFailure.NetworkUnavailable:
                    return "NETWORK UNAVAILABLE";
                case AuthenticationFailure.ProviderUnavailable:
                    return "GOOGLE SIGN UP REQUIRES BACKND";
                case AuthenticationFailure.OperationInProgress:
                    return "LOGIN IS ALREADY RUNNING";
                case AuthenticationFailure.NoSavedSession:
                    return "SELECT LOGIN METHOD";
                default:
                    return string.IsNullOrWhiteSpace(result.Message)
                        ? "LOGIN FAILED"
                        : result.Message.ToUpperInvariant();
            }
        }

        private void SetStatusTextAlpha(float alpha)
        {
            if (statusText == null)
            {
                return;
            }

            Color textColor = statusText.color;
            textColor.a = Mathf.Clamp01(alpha);
            statusText.color = textColor;
        }

        private bool IsLobbyReady()
        {
            return lobbyLoadOperation == null
                || lobbyLoadOperation.progress >= 0.9f;
        }

        private static bool WasTouchPressed()
        {
            if (Touchscreen.current != null
                && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                return true;
            }

            if (Mouse.current != null
                && Mouse.current.leftButton.wasPressedThisFrame)
            {
                return true;
            }

            return Keyboard.current != null
                && (Keyboard.current.spaceKey.wasPressedThisFrame
                    || Keyboard.current.enterKey.wasPressedThisFrame);
        }

        private void BuildTitleUI()
        {
            Transform existingRoot = transform.Find(RuntimeRootName);
            if (existingRoot != null)
            {
                Destroy(existingRoot.gameObject);
            }

            GameObject rootObject = new GameObject(
                RuntimeRootName,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            rootObject.transform.SetParent(transform, false);

            Canvas canvas = rootObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = rootObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode =
                CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 2400f);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform root = rootObject.GetComponent<RectTransform>();
            Image background = CreateImage(
                root,
                "Background",
                Vector2.zero,
                Vector2.one,
                Color.white,
                backgroundSprite);
            if (backgroundSprite != null)
            {
                AspectRatioFitter backgroundFitter =
                    background.gameObject.AddComponent<AspectRatioFitter>();
                backgroundFitter.aspectMode =
                    AspectRatioFitter.AspectMode.EnvelopeParent;
                backgroundFitter.aspectRatio =
                    backgroundSprite.rect.width / backgroundSprite.rect.height;
            }

            CreateLoginPanel(root);

            RectTransform loadingBarRoot = CreateImage(
                root,
                "LoadingBar",
                new Vector2(0.18f, 0.07f),
                new Vector2(0.82f, 0.095f),
                loadingBarBackgroundColor,
                null).rectTransform;

            Image fill = CreateImage(
                loadingBarRoot,
                "Fill",
                Vector2.zero,
                new Vector2(0f, 1f),
                loadingBarFillColor,
                null);
            loadingBarFillRect = fill.rectTransform;
            loadingBarFillRect.pivot = new Vector2(0f, 0.5f);
            loadingBarFillRect.offsetMin = new Vector2(5f, 5f);
            loadingBarFillRect.offsetMax = new Vector2(-5f, -5f);

            statusText = CreateText(
                root,
                "StatusText",
                "LOADING 0%",
                new Vector2(0.18f, 0.105f),
                new Vector2(0.82f, 0.16f),
                48);
        }

        private void SetLoadingProgress(float progress)
        {
            progress = Mathf.Clamp01(progress);
            if (loadingBarFillRect != null)
            {
                loadingBarFillRect.anchorMax =
                    new Vector2(progress, 1f);
            }

            if (statusText != null
                && !isReadyForTouch
                && !isLoginPanelVisible
                && !isAuthenticationOperationActive)
            {
                statusText.text =
                    $"LOADING {Mathf.RoundToInt(progress * 100f)}%";
            }
        }

        private void CreateLoginPanel(RectTransform root)
        {
            Image panelImage = CreateImage(
                root,
                "LoginPanel",
                new Vector2(0.13f, 0.22f),
                new Vector2(0.87f, 0.73f),
                new Color(0.02f, 0.025f, 0.035f, 0.86f),
                null);
            panelImage.raycastTarget = true;
            loginPanelRoot = panelImage.rectTransform;

            CreateText(
                loginPanelRoot,
                "LoginTitle",
                "LOGIN",
                new Vector2(0.08f, 0.87f),
                new Vector2(0.92f, 0.97f),
                42);
            loginMessageText = CreateText(
                loginPanelRoot,
                "LoginMessage",
                "SELECT LOGIN METHOD",
                new Vector2(0.08f, 0.77f),
                new Vector2(0.92f, 0.87f),
                24);

            guestLoginRoot = CreateRectTransform(
                loginPanelRoot,
                "GuestLoginRoot",
                Vector2.zero,
                Vector2.one);
            googleSignupButton = CreateButton(
                guestLoginRoot,
                "GoogleSignupButton",
                "GOOGLE LOGIN",
                new Vector2(0.08f, 0.68f),
                new Vector2(0.92f, 0.76f),
                OnGoogleSignupButtonPressed);
            accountFlowTitleText = CreateText(
                guestLoginRoot,
                "GuestSectionTitle",
                "ACCOUNT LOGIN",
                new Vector2(0.08f, 0.60f),
                new Vector2(0.92f, 0.67f),
                25);

            accountIdInput = CreateInputField(
                guestLoginRoot,
                "AccountIdInput",
                "ACCOUNT ID",
                new Vector2(0.08f, 0.46f),
                new Vector2(0.92f, 0.57f),
                false,
                20);
            passwordInput = CreateInputField(
                guestLoginRoot,
                "PasswordInput",
                "PASSWORD",
                new Vector2(0.08f, 0.33f),
                new Vector2(0.92f, 0.44f),
                true,
                32);
            rememberCredentialsToggle = CreateToggle(
                guestLoginRoot,
                "RememberCredentialsToggle",
                "REMEMBER ID / PW",
                new Vector2(0.08f, 0.25f),
                new Vector2(0.92f, 0.32f),
                OnRememberCredentialsChanged);
            guestLoginButton = CreateButton(
                guestLoginRoot,
                "GuestLoginButton",
                "LOGIN",
                new Vector2(0.08f, 0.13f),
                new Vector2(0.92f, 0.23f),
                OnGuestLoginButtonPressed);
            showRegistrationButton = CreateButton(
                guestLoginRoot,
                "ShowRegistrationButton",
                "CREATE NEW ACCOUNT",
                new Vector2(0.08f, 0.02f),
                new Vector2(0.92f, 0.11f),
                OnShowRegistrationButtonPressed,
                24);
            registrationNextButton = CreateButton(
                guestLoginRoot,
                "RegistrationNextButton",
                "NEXT",
                new Vector2(0.08f, 0.13f),
                new Vector2(0.92f, 0.23f),
                OnRegistrationNextButtonPressed);
            backToLoginButton = CreateButton(
                guestLoginRoot,
                "BackToLoginButton",
                "BACK TO LOGIN",
                new Vector2(0.08f, 0.02f),
                new Vector2(0.92f, 0.11f),
                OnBackToLoginButtonPressed,
                24);

            nicknameSetupRoot = CreateRectTransform(
                loginPanelRoot,
                "NicknameSetupRoot",
                Vector2.zero,
                Vector2.one);
            CreateText(
                nicknameSetupRoot,
                "NicknameSetupTitle",
                "CREATE ACCOUNT - 2/2",
                new Vector2(0.08f, 0.60f),
                new Vector2(0.92f, 0.69f),
                28);
            CreateText(
                nicknameSetupRoot,
                "NicknameSetupDescription",
                "THIS NAME APPEARS IN GAME AND RANKINGS",
                new Vector2(0.08f, 0.53f),
                new Vector2(0.92f, 0.60f),
                20);
            nicknameInput = CreateInputField(
                nicknameSetupRoot,
                "NicknameInput",
                "NICKNAME",
                new Vector2(0.08f, 0.40f),
                new Vector2(0.65f, 0.51f),
                false,
                12);
            nicknameInput.onValueChanged.AddListener(OnNicknameChanged);
            nicknameCheckButton = CreateButton(
                nicknameSetupRoot,
                "NicknameCheckButton",
                "CHECK",
                new Vector2(0.68f, 0.40f),
                new Vector2(0.92f, 0.51f),
                OnNicknameCheckButtonPressed,
                21);
            guestRegisterButton = CreateButton(
                nicknameSetupRoot,
                "GuestRegisterButton",
                "CREATE ACCOUNT",
                new Vector2(0.08f, 0.19f),
                new Vector2(0.92f, 0.31f),
                OnGuestRegisterButtonPressed,
                26);
            backToRegistrationButton = CreateButton(
                nicknameSetupRoot,
                "BackToRegistrationButton",
                "BACK",
                new Vector2(0.08f, 0.06f),
                new Vector2(0.92f, 0.16f),
                OnBackToRegistrationButtonPressed,
                24);

            googleLoginRoot = CreateRectTransform(
                loginPanelRoot,
                "GoogleLoginRoot",
                new Vector2(0.08f, 0.09f),
                new Vector2(0.92f, 0.75f));
            CreateText(
                googleLoginRoot,
                "GoogleLoginTitle",
                "GOOGLE SIGN UP",
                new Vector2(0f, 0.58f),
                new Vector2(1f, 0.82f),
                34);
            CreateText(
                googleLoginRoot,
                "GoogleLoginDescription",
                "BACKND CONNECTION REQUIRED",
                new Vector2(0f, 0.38f),
                new Vector2(1f, 0.58f),
                23);
            backToGuestButton = CreateButton(
                googleLoginRoot,
                "BackToGuestButton",
                "BACK TO ACCOUNT",
                new Vector2(0.12f, 0.10f),
                new Vector2(0.88f, 0.30f),
                OnBackToGuestButtonPressed,
                26);
            nicknameSetupRoot.gameObject.SetActive(false);
            googleLoginRoot.gameObject.SetActive(false);
            RestoreCredentialPreference();
            SetAccountEntryMode(false);
            loginPanelRoot.gameObject.SetActive(false);
        }

        private static RectTransform CreateRectTransform(
            RectTransform parent,
            string objectName,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            GameObject childObject =
                new GameObject(objectName, typeof(RectTransform));
            childObject.transform.SetParent(parent, false);

            RectTransform rect = childObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static Toggle CreateToggle(
            RectTransform parent,
            string objectName,
            string label,
            Vector2 anchorMin,
            Vector2 anchorMax,
            UnityEngine.Events.UnityAction<bool> onValueChanged)
        {
            RectTransform root = CreateRectTransform(
                parent,
                objectName,
                anchorMin,
                anchorMax);

            Image background = CreateImage(
                root,
                "Background",
                new Vector2(0f, 0.16f),
                new Vector2(0.09f, 0.84f),
                new Color(1f, 1f, 1f, 0.2f),
                null);
            background.raycastTarget = true;
            Image checkmark = CreateImage(
                background.rectTransform,
                "Checkmark",
                new Vector2(0.18f, 0.18f),
                new Vector2(0.82f, 0.82f),
                new Color(0.96f, 0.82f, 0.22f, 1f),
                null);
            Text labelText = CreateText(
                root,
                "Label",
                label,
                new Vector2(0.12f, 0f),
                Vector2.one,
                22);
            labelText.alignment = TextAnchor.MiddleLeft;

            Toggle toggle = root.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = checkmark;
            if (onValueChanged != null)
            {
                toggle.onValueChanged.AddListener(onValueChanged);
            }

            return toggle;
        }

        private static InputField CreateInputField(
            RectTransform parent,
            string objectName,
            string placeholderText,
            Vector2 anchorMin,
            Vector2 anchorMax,
            bool isPassword,
            int characterLimit)
        {
            GameObject inputObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(InputField));
            inputObject.transform.SetParent(parent, false);

            RectTransform rect = inputObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = inputObject.GetComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.14f);

            Text text = CreateText(
                rect,
                "Text",
                string.Empty,
                new Vector2(0.04f, 0f),
                new Vector2(0.96f, 1f),
                28);
            text.alignment = TextAnchor.MiddleLeft;
            text.fontStyle = FontStyle.Normal;

            Text placeholder = CreateText(
                rect,
                "Placeholder",
                placeholderText,
                new Vector2(0.04f, 0f),
                new Vector2(0.96f, 1f),
                28);
            placeholder.alignment = TextAnchor.MiddleLeft;
            placeholder.fontStyle = FontStyle.Normal;
            placeholder.color = new Color(1f, 1f, 1f, 0.45f);

            InputField inputField = inputObject.GetComponent<InputField>();
            inputField.textComponent = text;
            inputField.placeholder = placeholder;
            inputField.targetGraphic = image;
            inputField.contentType = isPassword
                ? InputField.ContentType.Password
                : InputField.ContentType.Standard;
            inputField.characterLimit = Mathf.Max(0, characterLimit);
            return inputField;
        }

        private static Button CreateButton(
            RectTransform parent,
            string objectName,
            string label,
            Vector2 anchorMin,
            Vector2 anchorMax,
            UnityEngine.Events.UnityAction onClick,
            int fontSize = 30)
        {
            GameObject buttonObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.96f, 0.82f, 0.22f, 0.95f);

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            Text buttonText = CreateText(
                rect,
                "Label",
                label,
                Vector2.zero,
                Vector2.one,
                fontSize);
            buttonText.color = new Color(0.05f, 0.06f, 0.08f, 1f);
            return button;
        }

        private static Image CreateImage(
            RectTransform parent,
            string objectName,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Color color,
            Sprite sprite)
        {
            GameObject imageObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            imageObject.transform.SetParent(parent, false);

            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            image.sprite = sprite;
            image.preserveAspect = false;
            image.raycastTarget = false;
            return image;
        }

        private static Text CreateText(
            RectTransform parent,
            string objectName,
            string content,
            Vector2 anchorMin,
            Vector2 anchorMax,
            int fontSize)
        {
            GameObject textObject = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text));
            textObject.transform.SetParent(parent, false);

            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Text text = textObject.GetComponent<Text>();
            text.text = content;
            text.font =
                Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            new GameObject(
                "EventSystem",
                typeof(EventSystem),
                typeof(InputSystemUIInputModule));
        }
    }
}
