using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class sPointBanner : MonoBehaviour {

	// Use this for initialization
	void Start () {
		
	}
	
	// Update is called once per frame
	void Update () {
        // Ориентация баннера
        transform.LookAt(Camera.main.transform);
        // Масштабирование баннера
        float myDistance = (transform.position - Camera.main.transform.position).magnitude;
        float myScale = Mathf.Clamp(myDistance / 5000, 1.0f, 10.0f);
        transform.localScale = Vector3.one * myScale;
    }
}
