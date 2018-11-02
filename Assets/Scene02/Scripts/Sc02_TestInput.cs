using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Sc02_TestInput : MonoBehaviour {

	// Use this for initialization
	void Start () {
        StartCoroutine("TestInput");
    }
	
	// Update is called once per frame
	void Update () {
		
	}

    IEnumerator TestInput()
    {
        while (true)
        {
            yield return new WaitForSeconds(1);
            print("Hat_Vert = " + Input.GetAxis("Hat_Vert"));
        }
    }

}
