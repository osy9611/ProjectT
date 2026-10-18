using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ProjectT;
using ProjectT.Controller;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;

static partial class Program
{
    sealed class InputFixture
    {
        public Controller Controller;
        public InputActionAsset Asset;
        public InputAction Fire, Move, Click;
    }

    static InputFixture CreateInputFixture()
    {
        var asset = new InputActionAsset();
        var player = asset.Map;
        player.actions.Clear();
        var fire = new InputAction { name = "Fire", actionMap = player };
        var move = new InputAction { name = "Move", actionMap = player };
        player.actions.Add(fire);
        player.actions.Add(move);
        var menu = new InputActionMap { name = "Menu", actions = new List<InputAction>() };
        asset.Maps.Add("Menu", menu);
        var click = new InputAction { name = "Click", actionMap = menu };
        menu.actions.Add(click);
        var controller = new Controller();
        controller.Init(asset, "Player");
        controller.Enable();
        return new InputFixture { Controller = controller, Asset = asset, Fire = fire, Move = move, Click = click };
    }

    static UnityEngine.InputSystem.UI.InputSystemUIInputModule UIModule(InputManager input)
    {
        return (UnityEngine.InputSystem.UI.InputSystemUIInputModule)typeof(InputManager)
            .GetField("uiInputModule", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(input);
    }

    static UniTask RunControllerCase(string name, Action test)
    {
        return Run("input: " + name, () =>
        {
            test();
            return UniTask.CompletedTask;
        });
    }

    static void CheckInputTrace(List<string> trace, string expected)
    {
        Check(string.Join(",", trace) == expected, "Expected " + expected + "; received " + string.Join(",", trace));
    }

    static InputUser UserOf(InputManager input)
    {
        return (InputUser)typeof(InputManager)
            .GetField("user", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(input);
    }

    static void NextInputFrame(ManagerHost host)
    {
        UnityEngine.Time.frameCount++;
        host.Update(0f);
    }

    static string Paired(InputUser user)
    {
        var names = new List<string>();
        foreach (var device in user.pairedDevices)
            names.Add(device.GetType().Name);
        return string.Join(",", names);
    }

    static void CheckPublished(List<NotificationId> published, string expected)
    {
        Check(string.Join(",", published) == expected, "Expected " + expected + "; published " + string.Join(",", published));
        published.Clear();
    }

    static string Overrides(InputActionAsset asset)
    {
        var paths = new List<string>();
        foreach (var name in new[] { "UI/Submit", "UI/Cancel" })
        {
            foreach (var binding in asset.FindAction(name, true).bindings)
                paths.Add(binding.overridePath);
        }
        return string.Join("|", paths);
    }

    static async UniTask RunControllerTests()
    {
        await RunControllerDispatchTests();

        await RunControllerResetFailureTests();

        await RunControllerSetupTests();
    }
}
