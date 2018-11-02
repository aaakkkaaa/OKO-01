using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public class sTest01 : MonoBehaviour {

    // Структура для истории полетов
    public struct MyFlightHistory
    {
        public List<long> Time;
        public List<int> Position;
        //public List<Vector3> Euler;
        //public List<Vector3> Speed;
    }
    // Словарь - истории полетов. Ключ тот же, значения - массив List с координатами и временем
    Dictionary<int, MyFlightHistory> myPlaneHistory = new Dictionary<int, MyFlightHistory>();

    Stopwatch myStopWatch;  // Точное время
    long myStartTime;


    // Use this for initialization
    void Start () {

        myStopWatch = new Stopwatch();
        myStopWatch.Start();
        myStartTime = myStopWatch.ElapsedMilliseconds;

        for (int j = 1; j < 4; j++)
        {
            for (int i = 1; i < 5; i++)
            {
                MyFlightHistory myOnePlaneHist = new MyFlightHistory();

                print("j = " + j + " i = " + i);
                if (myPlaneHistory.ContainsKey(i))
                {
                    myOnePlaneHist = myPlaneHistory[i];
                    myOnePlaneHist.Time.Add(myStopWatch.ElapsedMilliseconds - myStartTime);
                    myOnePlaneHist.Position.Add(j * 10);
                    myPlaneHistory[i] = myOnePlaneHist;
                }
                else
                {
                    myOnePlaneHist.Time = new List<long>
                    {
                        myStopWatch.ElapsedMilliseconds - myStartTime
                    };
                    myOnePlaneHist.Position = new List<int>
                    {
                        j*10
                    };
                    myPlaneHistory.Add(i, myOnePlaneHist);
                }
            }
        }

        for (int i = 0; i < myPlaneHistory.Count; i++)
        {
            int myKey = i + 1;
            for (int j = 0; j < myPlaneHistory[myKey].Time.Count; j++)
            {
                print("key = " + myKey + ", " + myPlaneHistory[myKey].Time[j] + ", " + myPlaneHistory[myKey].Position[j]);
            }
        }


    }

    // Update is called once per frame
    void Update () {
		
	}
}
