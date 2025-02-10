using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ProjectT;
using System;

[Serializable]
public class OptionStorage : ClientLocalStorage
{
    public string TestName = "TEST";

    public override void CompleteSave()
    {
    }

    public override void CompleteLoad()
    {
        Debug.Log("OptionStorage Complete Load");
    }
}
