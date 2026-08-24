using Cysharp.Threading.Tasks;
using ProjectT;
using ProjectT.Skill;
using ProjectT.UGUI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Jobs;

[BurstCompile]
public struct ParallelJob : IJobParallelForTransform
{
    public float deltaTime;
    public void Execute(int index, TransformAccess transform)
    {
        transform.position += Vector3.up * deltaTime;
    }
}

public class TestReflection : MonoBehaviour
{
    //public Transform[] transforms;
    //// Start is called before the first frame update
    //void Start()
    //{
    //    NativeArray<int> datas = new NativeArray<int>(1, Allocator.TempJob);

    //    for(int i=0;i<datas.Count();++i)
    //    {
    //        datas[i] = i;
    //    }

    //    ParallelJob job = new ParallelJob
    //    {
    //        datas = datas
    //    };

    //    JobHandle handle = job.Schedule(datas.Length, 1);
    //    handle.Complete();

    //    for(int i=0;i<datas.Length;++i)
    //    {
    //        Global.Instance.Log($"Data[{i}] = {datas[i]}");
    //    }
    //    datas.Dispose();
    //}

    //public void GoTitle()
    //{
    //    Global.Scene.GoTitle();

    //}

    //bool testSkill = false;
    //public void CreateUI()
    //{
    //    Global.UI.CreateWidget<TestUI>(UIDefine.eUIType.Test);


    //}

    //TestActor actor;
    //SkillAgent skillAgent;
    //public void RegisterSkill()
    //{
    //    actor = new TestActor();
    //    skillAgent = new SkillAgent();
    //    skillAgent.Init(actor);
    //    skillAgent.AddBuff(6);

    //    skillAgent.ActionController.RegisterSkill(201);
    //}

    //public void StartSkill()
    //{
    //    skillAgent.ActionController.ActivateSkill(201);
    //}

    //public void Update()
    //{
    //    if (skillAgent != null)
    //        skillAgent.OnUpdate(Time.deltaTime);
    //}

    //public void LoadLocalStorage()
    //{
    //    OptionStorage optionStorage = Global.LocalStorage.GetData<OptionStorage>(EClientLocalStorageType.Option);

    //    Debug.Log(optionStorage.TestName);
    //}

    //public void CreateLocalStorage()
    //{
    //    OptionStorage optionStorage = Global.LocalStorage.CreateData<OptionStorage>(EClientLocalStorageType.Option);
    //    optionStorage.TestName = "TEST123";

    //    Global.LocalStorage.SaveData(EClientLocalStorageType.Option);
    //}
}