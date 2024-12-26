using DesignEnum;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectT.Sound
{
    public class UIClickSound : MonoBehaviour
    {
        [SerializeField] private string clickSoundType;
        public string ClickSoundType { get => clickSoundType; set => clickSoundType = value; }

        public bool isToggle = false;
        private bool isOn = true;
        private bool reRegister = false;
        private bool isStart = false;

        private void Awake()
        {
            GetUIComponent(false);
        }

        private void Start()
        {
            isOn = true;
            isStart = true;
        }

        public void GetUIComponent(bool reRegister)
        {
            this.reRegister = reRegister;

            Button button = GetComponent<Button>();

            if (button != null)
            {
                button.onClick.AddListener(PlayClickSound);
            }

            EventTrigger eventTrigger = null;
            if(isToggle)
            {
                isOn = false;
                Toggle toggle = GetComponent<Toggle>();
                if(toggle != null)
                {
                    eventTrigger = GetComponent<EventTrigger>();
                    if (eventTrigger != null)
                        Destroy(eventTrigger);

                    toggle.onValueChanged.AddListener((isOn) =>
                    {
                        if (isOn)
                            PlayClickSound();
                    });

                    return;
                }
            }

            eventTrigger = GetComponent<EventTrigger>();
            if(eventTrigger != null)
            {
                EventTrigger.Entry clickEntry = eventTrigger.triggers.Find(elem => elem.eventID == EventTriggerType.PointerClick);

                if(clickEntry == null)
                {
                    clickEntry = new EventTrigger.Entry();
                    clickEntry.eventID = EventTriggerType.PointerClick;
                    eventTrigger.triggers.Add(clickEntry);
                }

                clickEntry.callback.AddListener(elem => PlayClickSound());
            }
        }

        private void PlayClickSound()
        {
            if (reRegister && isStart)
            {
                isOn = true;
            }

            if (isOn)
            {
                SoundList soundList = (SoundList)System.Enum.Parse(typeof(SoundList), clickSoundType);
                //TODO : 사운드 재생 해야함
            }
            else
            {

            }
        }
    }
}
