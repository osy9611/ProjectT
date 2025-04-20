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
        Debug.Log("��� ���");
    }
    public void FuncTest(int value)
    {
        Debug.Log("�Ű����� �� �߰��� ���, �Ű����� = " + value);
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
        //// SetValue�� �Ű����� Ÿ���� ������Ʈ�� �ޱ� ������ �ڽ��� ���ص� � ������Ÿ�Ե� SetValue�� �� �� �ִ�
        //// �� �������� ���ο��� ���÷����� �Ͼ�� �ִ°�!! �ֳ��ϸ� �ڽ̰� ��ڽ����ϱ� ���ؼ��� Ŭ������ ���� ��Ÿ ������ �˰� �־�� �ϱ� ����.

        Debug.Log(type.GetField("currentHP").GetValue(temp));
        Debug.Log(type.GetField("currentHP").GetValue(temp2));
        Debug.Log(type.GetField("currentMana").GetValue(temp));
        //// GetValue() �Լ��� ���ڷ� temp �� �������ָ� GetValue ���¿��� � �༮�� hp�� ���������� �𸥴�
        //// �ֳ��ϸ� GetField �����δ� Class������ ��Ÿ �������� ������ �ֱ� ����

        // �Ϲ��� �Լ� ȣ��
        type.GetMethod("Func").Invoke(temp, null);


        // �Ű����� ���� �ִ� �Լ� ȣ��
        object[] args = new object[1];
        args[0] = 50;
        type.GetMethod("FuncTest")?.Invoke(temp, args);
        // Invoke �� �ι�° ���ڰ��� ������Ʈ�� �迭�� �ޱ� ������ �̷������� Ȱ���� �����ϴ�


        // return ���� �ִ� �Լ� ȣ��
        var result = type.GetMethod("FuncReturnValue")?.Invoke(temp, null); // ������Ʈ ���·� �������ֱ� ������
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