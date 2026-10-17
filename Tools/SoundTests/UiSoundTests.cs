using ProjectT;
using ProjectT.Sound;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal static partial class Program
{
    private static void UiRegistrationLifecycle()
    {
        var setup = Setup();
        var go = new GameObject("Button");
        var button = go.AddComponent<Button>();
        var click = go.AddComponent<UIClickSound>();
        click.ClickSoundType = "UI";
        var outside = 0;
        button.onClick.AddListener(() => outside++);

        Invoke(click, "OnEnable");
        button.onClick.Invoke();
        Check(Source(setup.manager, eSound.UI).OneShots.Count == 0, "UI sound played before Start.");
        Invoke(click, "Start");
        click.GetUIComponent(true);
        click.GetUIComponent(true);
        button.onClick.Invoke();
        Check(Source(setup.manager, eSound.UI).OneShots.Count == 1, "Re-registration duplicated or lost the click callback.");
        Check(outside == 2, "Sound registration disturbed an existing button listener.");

        Invoke(click, "OnDisable");
        button.onClick.Invoke();
        Check(Source(setup.manager, eSound.UI).OneShots.Count == 1, "Disabled UI retained its sound callback.");
        Check(outside == 3 && button.onClick.ListenerCount == 1, "Disable removed or duplicated an outside listener.");
    }

    private static void UiDisabledDuringEventIsSilent()
    {
        var setup = Setup();
        var go = new GameObject("DisableDuringClick");
        var button = go.AddComponent<Button>();
        var click = go.AddComponent<UIClickSound>();
        click.ClickSoundType = "UI";
        var outside = 0;
        button.onClick.AddListener(() =>
        {
            outside++;
            click.enabled = false;
            Invoke(click, "OnDisable");
        });
        Invoke(click, "OnEnable");
        Invoke(click, "Start");

        button.onClick.Invoke();

        Check(outside == 1, "The preceding listener did not run.");
        Check(Source(setup.manager, eSound.UI).OneShots.Count == 0, "An invocation snapshot played sound after the component was disabled.");
        Check(button.onClick.ListenerCount == 1, "Disable disturbed the preceding listener.");
    }

    private static void ToggleRouteAndVariants()
    {
        var setup = Setup();
        var go = new GameObject("Toggle");
        var button = go.AddComponent<Button>();
        var toggle = go.AddComponent<Toggle>();
        var click = go.AddComponent<UIClickSound>();
        click.isToggle = true;
        SetField(click, "clickSound", new AudioClip("On"));
        SetField(click, "toggleOffSound", new AudioClip("Off"));
        Invoke(click, "OnEnable");
        Invoke(click, "Start");

        button.onClick.Invoke();
        Check(Source(setup.manager, eSound.UI).OneShots.Count == 0, "Toggle mode also registered the button route.");
        toggle.onValueChanged.Invoke(true);
        toggle.onValueChanged.Invoke(false);
        var shots = Source(setup.manager, eSound.UI).OneShots;
        Check(shots.Count == 2 && shots[0].name == "On" && shots[1].name == "Off", "Toggle on/off sounds did not use their configured variants.");
    }

    private static void EventTriggerPreservesOtherListeners()
    {
        var setup = Setup();
        var go = new GameObject("Trigger");
        var trigger = go.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
        var outside = 0;
        entry.callback.AddListener(_ => outside++);
        trigger.triggers.Add(entry);
        var click = go.AddComponent<UIClickSound>();
        click.ClickSoundType = "UI";

        Invoke(click, "OnEnable");
        Invoke(click, "Start");
        entry.callback.Invoke(new BaseEventData());
        Check(outside == 1 && Source(setup.manager, eSound.UI).OneShots.Count == 1, "EventTrigger did not invoke both listeners.");
        Invoke(click, "OnDisable");
        entry.callback.Invoke(new BaseEventData());
        Check(outside == 2 && entry.callback.ListenerCount == 1, "Disable removed the EventTrigger entry or another owner's listener.");
    }
}
