using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectT.Pool
{
    public interface IPoolable
    {
        void OnReturn();
        void OnGet();
    }
}