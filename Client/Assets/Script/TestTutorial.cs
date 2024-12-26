using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class TestTutorial : MonoBehaviour
{
    public List<GameObject> objs;
    public List<GameObject> pivotObjs;

    private int currentPage;

    Coroutine scrollCoroutine;

    public float Speed;

    private enum ePivot
    {
        Left,
        Mid,
        Right
    }

    private void Start()
    {

    }

    public void OnClick_Scroll(int index)
    {
        if (currentPage == index)
            return;

        int currentPageMoveIndex = 0;
        int targetPageMoveIndex = 0;

        Transform currentPageTr = objs[currentPage].transform;
        Transform targetPageTr = objs[index].transform;


        objs[currentPage].SetActive(false);
        objs[index].SetActive(false);

        //Current Mid에서 Left로
        //Target Right에서 Mid로
        if (currentPage < index)
        {
            currentPageTr.position = pivotObjs[(int)ePivot.Mid].transform.position;
            targetPageTr.position = pivotObjs[(int)ePivot.Right].transform.position;


            currentPageMoveIndex = (int)ePivot.Left;
            targetPageMoveIndex = (int)ePivot.Mid;
        }
        else
        {
            currentPageTr.position = pivotObjs[(int)ePivot.Mid].transform.position;
            targetPageTr.position = pivotObjs[(int)ePivot.Left].transform.position;

            currentPageMoveIndex = (int)ePivot.Right;
            targetPageMoveIndex = (int)ePivot.Mid;
        }

        if (scrollCoroutine != null)
            StopCoroutine(scrollCoroutine);

        scrollCoroutine = StartCoroutine(CoScroll(currentPage, index, currentPageMoveIndex, targetPageMoveIndex));
        currentPage = index;
    }

    public void Scroll()
    {

    }

    private IEnumerator CoScroll(int currentPage, int targetPage, int currentPageMoveIndex, int targetPageMoveIndex)
    {
        Transform currentPageTr = objs[currentPage].transform;
        Transform targetPageTr = objs[targetPage].transform;


        objs[currentPage].SetActive(true);
        objs[targetPage].SetActive(true);

        while (true)
        {
            currentPageTr.position = Vector3.MoveTowards(currentPageTr.position, pivotObjs[currentPageMoveIndex].transform.position, Speed * Time.deltaTime);
            targetPageTr.position = Vector3.MoveTowards(targetPageTr.position, pivotObjs[targetPageMoveIndex].transform.position, Speed * Time.deltaTime);

            if (currentPageTr.position == pivotObjs[currentPageMoveIndex].transform.position && targetPageTr.position == pivotObjs[targetPageMoveIndex].transform.position)
                break;


            yield return null;
        }


        for(int i = 0; i < objs.Count;++i)
        {
            if (i == targetPage)
                continue;

            objs[i].SetActive(false);
        }
    }
}
