using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectT.Sound
{
    public class UIClickSound : MonoBehaviour
    {
        [SerializeField, Tooltip("Addressable AudioClip path used when Click Sound is not assigned.")]
        private string clickSoundType;
        [SerializeField] private AudioClip clickSound;
        [SerializeField] private AudioClip toggleOffSound;

        public string ClickSoundType { get => clickSoundType; set => clickSoundType = value; }

        public bool isToggle = false;

        private Button button;
        private Toggle toggle;
        private EventTrigger.Entry pointerClickEntry;
        private bool isRegistered;
        private bool isStarted;

        private void OnEnable()
        {
            if (isStarted)
                RegisterInternal();
        }

        private void Start()
        {
            isStarted = true;
            RegisterInternal();
        }

        private void OnDisable()
        {
            UnregisterInternal();
        }

        public void GetUIComponent(bool reRegister)
        {
            if (reRegister)
                UnregisterInternal();

            if (isActiveAndEnabled && isStarted)
                RegisterInternal();
        }

        private void RegisterInternal()
        {
            if (isRegistered)
                return;

            if (isToggle)
            {
                toggle = GetComponent<Toggle>();
                if (toggle != null)
                {
                    toggle.onValueChanged.AddListener(PlayToggleSound);
                    isRegistered = true;
                    return;
                }
            }

            button = GetComponent<Button>();
            if (button != null)
            {
                button.onClick.AddListener(PlayClickSound);
                isRegistered = true;
                return;
            }

            EventTrigger eventTrigger = GetComponent<EventTrigger>();
            if (eventTrigger == null)
                return;

            pointerClickEntry = eventTrigger.triggers.Find(entry => entry.eventID == EventTriggerType.PointerClick);
            if (pointerClickEntry == null)
            {
                pointerClickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                eventTrigger.triggers.Add(pointerClickEntry);
            }

            pointerClickEntry.callback.AddListener(PlayPointerClickSound);
            isRegistered = true;
        }

        private void UnregisterInternal()
        {
            if (!isRegistered)
                return;

            if (toggle != null)
                toggle.onValueChanged.RemoveListener(PlayToggleSound);

            if (button != null)
                button.onClick.RemoveListener(PlayClickSound);

            if (pointerClickEntry != null)
                pointerClickEntry.callback.RemoveListener(PlayPointerClickSound);

            button = null;
            toggle = null;
            pointerClickEntry = null;
            isRegistered = false;
        }

        private void PlayClickSound()
        {
            PlayInternal(clickSound, clickSoundType);
        }

        private void PlayToggleSound(bool value)
        {
            if (value)
            {
                PlayClickSound();
                return;
            }

            PlayInternal(toggleOffSound, null);
        }

        private void PlayPointerClickSound(BaseEventData _)
        {
            PlayClickSound();
        }

        private void PlayInternal(AudioClip clip, string path)
        {
            if (!isActiveAndEnabled)
                return;

            if (!Global.TryGetReady<SoundManager>(out _))
                return;

            if (clip != null)
            {
                Global.Sound.Play(clip, eSound.UI);
                return;
            }

            if (string.IsNullOrWhiteSpace(path) || string.Equals(path, "None", StringComparison.OrdinalIgnoreCase))
                return;

            Global.Sound.Play(path.Trim(), eSound.UI);
        }
    }
}
