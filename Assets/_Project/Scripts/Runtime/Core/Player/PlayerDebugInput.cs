using System;
using LootUp.Core.Characters;
using LootUp.Core.Rising;
using LootUp.Core.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LootUp.Core.Player
{
    [DefaultExecutionOrder(-100)]
    public sealed class PlayerDebugInput : MonoBehaviour
    {
        private const float TimeCheatSeconds = 30f;

        private Func<GameObject> playerResolver;
        private UnityEngine.Object logContext;

        public void Configure(Func<GameObject> resolver, UnityEngine.Object context)
        {
            playerResolver = resolver;
            logContext = context;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.digit1Key.wasPressedThisFrame
                || keyboard.numpad1Key.wasPressedThisFrame)
            {
                TriggerFeverTest();
            }

            if (keyboard.digit2Key.wasPressedThisFrame
                || keyboard.numpad2Key.wasPressedThisFrame)
            {
                TriggerAddTimeTest();
            }

            if (keyboard.digit3Key.wasPressedThisFrame
                || keyboard.numpad3Key.wasPressedThisFrame)
            {
                TriggerHealTest();
            }

            if (keyboard.digit4Key.wasPressedThisFrame
                || keyboard.numpad4Key.wasPressedThisFrame)
            {
                TriggerRisingDrawTest();
            }
        }

        private void TriggerFeverTest()
        {
            Debug.Log("Fever test input detected", logContext);

            GameObject player = playerResolver?.Invoke();
            if (player == null)
            {
                Debug.LogWarning("피버 테스트를 실행할 Player가 생성되지 않았습니다.", logContext);
                return;
            }

            PlayerCharacterRuntime characterRuntime = player.GetComponent<PlayerCharacterRuntime>();
            if (characterRuntime == null)
            {
                Debug.LogWarning("피버 테스트를 실행할 PlayerCharacterRuntime을 찾을 수 없습니다.", logContext);
                return;
            }

            characterRuntime.FillFeverGaugeForTest();
        }

        private void TriggerAddTimeTest()
        {
            TopHUDController topHUDController = FindFirstObjectByType<TopHUDController>();
            if (topHUDController == null)
            {
                Debug.LogWarning("시간 연장 테스트를 실행할 TopHUDController를 찾을 수 없습니다.", logContext);
                return;
            }

            topHUDController.AddTime(TimeCheatSeconds);
            Debug.Log($"Time test input detected: +{TimeCheatSeconds:0}s", logContext);
        }

        private void TriggerHealTest()
        {
            GameObject player = playerResolver?.Invoke();
            if (player == null)
            {
                Debug.LogWarning("생명력 회복 테스트를 실행할 Player가 생성되지 않았습니다.", logContext);
                return;
            }

            PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();
            if (playerHealth == null)
            {
                Debug.LogWarning("생명력 회복 테스트를 실행할 PlayerHealth를 찾을 수 없습니다.", logContext);
                return;
            }

            int healedLife = playerHealth.Heal(1);
            Debug.Log($"Life heal test input detected: +{healedLife} HP", logContext);
        }

        private void TriggerRisingDrawTest()
        {
            RisingSystem risingSystem = FindFirstObjectByType<RisingSystem>();
            if (risingSystem == null)
            {
                Debug.LogWarning("RISING 테스트를 실행할 RisingSystem을 찾을 수 없습니다.", logContext);
                return;
            }

            risingSystem.AcquireRandomLetterForDebug();
        }
    }
}
