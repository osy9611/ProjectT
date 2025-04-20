using Cysharp.Threading.Tasks;
using ProjectT;
using ProjectT.Skill;
using ProjectT.UGUI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public class Temp
{
    public string Name { get; set; }
    public int Age { get; set; }

    public float currentHP;
    public float currentMana;
    public string name;

    public void Func()
    {
        Debug.Log("어떠한 기능");
    }
    public void FuncTest(int value)
    {
        Debug.Log("매개변수 를 추가한 기능, 매개변수 = " + value);
    }

    public int FuncReturnValue()
    {
        return 10;
    }

}

public class TestReflection : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        Temp temp = new Temp();
        Temp temp2 = new Temp();

        temp.currentHP = 100;
        temp2.currentHP = 300;

        Type type;
        type = temp.GetType();


        type.GetField("currentHP").SetValue(temp, 400);
        type.GetField("name").SetValue(temp, "Test");
        type.GetField("currentMana").SetValue(temp, 30.5f);
        //// SetValue의 매개변수 타입은 오브젝트로 받기 때문에 박싱을 통해들어가 어떤 데이터타입도 SetValue로 들어갈 수 있다
        //// 이 과정에서 내부에서 리플렉션이 일어나고 있는것!! 왜냐하면 박싱과 언박싱을하기 위해서는 클래스에 대한 메타 정보를 알고 있어야 하기 때문.

        Debug.Log(type.GetField("currentHP").GetValue(temp));
        Debug.Log(type.GetField("currentHP").GetValue(temp2));
        Debug.Log(type.GetField("currentMana").GetValue(temp));
        //// GetValue() 함수의 인자로 temp 를 안적어주면 GetValue 상태에서 어떤 녀석의 hp를 가져와줄지 모른다
        //// 왜냐하면 GetField 만으로는 Class에대한 메타 정보만을 가져와 주기 떄문

        // 일반적 함수 호출
        type.GetMethod("Func").Invoke(temp, null);


        // 매개변수 값이 있는 함수 호출
        object[] args = new object[1];
        args[0] = 50;
        type.GetMethod("FuncTest")?.Invoke(temp, args);
        // Invoke 의 두번째 인자값은 오브젝트의 배열을 받기 때문에 이런식으로 활용이 가능하다


        // return 값이 있는 함수 호출
        var result = type.GetMethod("FuncReturnValue")?.Invoke(temp, null); // 오브젝트 형태로 리턴해주기 때문에
        Debug.Log((int)result);

    }

    private async UniTask TESTUNI()
    {

        int Count = 0;
        int TESTCount = 10;

        while (Count < TESTCount)
        {
            await UniTask.WaitForSeconds(1.0f);
            Count++;
            Global.Instance.Log($"TEST {Count}");
        }
    }

    public void GoTitle()
    {
        Global.Scene.GoTitle();

    }

    bool testSkill = false;
    public void CreateUI()
    {
        Global.UI.CreateWidget<TestUI>(UIDefine.eUIType.Test);


    }

    TestActor actor;
    SkillAgent skillAgent;
    public void RegisterSkill()
    {
        actor = new TestActor();
        skillAgent = new SkillAgent();
        skillAgent.Init(actor);
        skillAgent.AddBuff(6);

        skillAgent.ActionController.RegisterAbility(201);
    }

    public void StartSkill()
    {
        skillAgent.ActionController.ActivateSkill(201);
    }

    public void Update()
    {
        if (skillAgent != null)
            skillAgent.OnUpdate(Time.deltaTime);
    }

    public void LoadLocalStorage()
    {
        OptionStorage optionStorage = Global.LocalStorage.GetData<OptionStorage>(EClientLocalStorageType.Option);

        Debug.Log(optionStorage.TestName);
    }

    public void CreateLocalStorage()
    {
        OptionStorage optionStorage = Global.LocalStorage.CreateData<OptionStorage>(EClientLocalStorageType.Option);
        optionStorage.TestName = "TEST123";

        Global.LocalStorage.SaveData(EClientLocalStorageType.Option);
    }
}