using System;
using LootUp.Core.Player;
using UnityEngine;

namespace LootUp.Core.Rising
{
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(RectTransform))]
    public sealed class RisingLetterPickup : MonoBehaviour
    {
        private readonly Vector3[] pickupCorners = new Vector3[4];
        private readonly Vector3[] playerCorners = new Vector3[4];
        private RectTransform rectTransform;
        private PlayerMotor playerMotor;
        private Action<RisingLetterPickup> collectedCallback;
        private float animationStartedAt;
        private bool collected;

        public int LetterTypeIndex { get; private set; }

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
        }

        private void Update()
        {
            if (collected || playerMotor == null)
            {
                return;
            }

            AnimatePopup();
            if (!Overlaps(playerMotor.RectTransform))
            {
                return;
            }

            collected = true;
            collectedCallback?.Invoke(this);
        }

        public void Configure(int letterTypeIndex, PlayerMotor motor, Action<RisingLetterPickup> onCollected)
        {
            rectTransform ??= GetComponent<RectTransform>();
            LetterTypeIndex = Mathf.Clamp(letterTypeIndex, 0, RisingConstants.LetterTypeCount - 1);
            playerMotor = motor;
            collectedCallback = onCollected;
            animationStartedAt = Time.unscaledTime;
            collected = false;
            rectTransform.localScale = Vector3.one * 0.2f;
        }

        private void AnimatePopup()
        {
            float elapsed = Mathf.Max(0f, Time.unscaledTime - animationStartedAt);
            float pop = Mathf.Clamp01(elapsed / 0.22f);
            float easedPop = 1f - Mathf.Pow(1f - pop, 3f);
            float pulse = 1f + Mathf.Sin(elapsed * 6f) * 0.05f;
            rectTransform.localScale = Vector3.one * easedPop * pulse;
        }

        private bool Overlaps(RectTransform playerRect)
        {
            if (rectTransform == null || playerRect == null)
            {
                return false;
            }

            rectTransform.GetWorldCorners(pickupCorners);
            playerRect.GetWorldCorners(playerCorners);
            Rect pickupBounds = CreateWorldRect(pickupCorners);
            Rect playerBounds = CreateWorldRect(playerCorners);
            return pickupBounds.Overlaps(playerBounds);
        }

        private static Rect CreateWorldRect(Vector3[] corners)
        {
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }
    }
}
