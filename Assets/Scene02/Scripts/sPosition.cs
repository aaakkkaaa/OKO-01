using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mapbox.Unity.Utilities;
using Mapbox.Utils;

public class sPosition : MonoBehaviour {

    // Точка IF (запад): -15694.41, 600, -4068.391
    // Точка IF (восток): 15400.55, 600, 3940.391
    [SerializeField]
    String GradMinSec = "";
    [SerializeField]
    bool UseGradMinSec;
    [SerializeField]
    float goToLatitude = 56.00777778f;
    [SerializeField]
    float goToLongitude = 37.66027778f;
    [SerializeField]
    float goToAltitude = 600;

    sFlightRadar myFlightRadar;

    // Use this for initialization
    void Start () {

        myFlightRadar = GameObject.Find("Boss").GetComponent<sFlightRadar>();
        
    }

    // Update is called once per frame
    void Update () {
        if (Input.GetKeyDown("1"))
        {
            if (UseGradMinSec)
            {
                String[] myGrMinSec = GradMinSec.Split(new Char[] { ' ', ',', '\'', 'N', 'E' });
                print("GradMinSec = " + GradMinSec);
                String myRange = "grad";
                for (int i = 0; i < myGrMinSec.Length; i++)
                {
                    print("i = " + i + " myGrMinSec = " + myGrMinSec[i]);
                    int myValue;
                    if (int.TryParse(myGrMinSec[i], out myValue))
                    {
                        if(i < 3)
                        {
                            switch (myRange)
                            {
                                case "grad":
                                    goToLatitude = myValue;
                                    print("myValue = " + myValue + " goToLatitude = " + goToLatitude);
                                    myRange = "min";
                                    break;
                                case "min":
                                    goToLatitude += myValue / 60.0f;
                                    print("myValue = " + myValue + " goToLatitude = " + goToLatitude);
                                    myRange = "sec";
                                    break;
                                case "sec":
                                    goToLatitude += myValue / 3600.0f;
                                    print("myValue = " + myValue + " goToLatitude = " + goToLatitude);
                                    myRange = "grad";
                                    break;
                            }
                        }
                        else
                        {
                            switch (myRange)
                            {
                                case "grad":
                                    goToLongitude = myValue;
                                    print("myValue = " + myValue + " goToLongitude = " + goToLongitude);
                                    myRange = "min";
                                    break;
                                case "min":
                                    goToLongitude += myValue / 60.0f;
                                    print("myValue = " + myValue + " goToLongitude = " + goToLongitude);
                                    myRange = "sec";
                                    break;
                                case "sec":
                                    goToLongitude += myValue / 3600.0f;
                                    print("myValue = " + myValue + " goToLongitude = " + goToLongitude);
                                    myRange = "grad";
                                    break;
                            }
                        }
                    }
                }
            }


            Vector2d worldPosition = Conversions.GeoToWorldPosition(goToLatitude, goToLongitude, myFlightRadar.myCenterMercator, myFlightRadar.myWorldRelativeScale);
            Vector3 myPosition;
            myPosition.x = (float)worldPosition.x;
            myPosition.y = goToAltitude;
            myPosition.z = (float)worldPosition.y;
            myPosition += myFlightRadar.myPosShift;
            transform.position = myPosition;
            print("My Position = " + myPosition.x + ", " + myPosition.y + ", " + myPosition.z);

        }
    }
}
