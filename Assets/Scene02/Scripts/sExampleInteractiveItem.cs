using System;
using UnityEngine;
using UnityEngine.UI;
using VRStandardAssets.Utils;

namespace VRStandardAssets.Examples
{
    // This script is a simple example of how an interactive item can
    // be used to change things on gameobjects by handling events.
    public class sExampleInteractiveItem : MonoBehaviour
    {
        [SerializeField] private Material m_NormalMaterial;                
        [SerializeField] private Material m_OverMaterial;                  
        [SerializeField] private Material m_ClickedMaterial;               
        [SerializeField] private Material m_DoubleClickedMaterial;         
        [SerializeField] private VRInteractiveItem m_InteractiveItem;
        [SerializeField] private Renderer m_Renderer;


        [SerializeField] private sFlightRadar myFlightRadarScript;

        String myPlaneName;
        Image myBannerImg;
        Color myBannerColor; // 5C2C0F82

        private void Awake ()
        {
            m_Renderer.material = m_NormalMaterial;
        }


        private void OnEnable()
        {
            m_InteractiveItem.OnOver += HandleOver;
            m_InteractiveItem.OnOut += HandleOut;
            m_InteractiveItem.OnClick += HandleClick;
            m_InteractiveItem.OnDoubleClick += HandleDoubleClick;

            myPlaneName = transform.parent.parent.name;
            myBannerImg = transform.parent.Find("Panel").GetComponent<Image>();
            //Debug.Log("myBannerImg = " + myBannerImg);
            myBannerColor = myBannerImg.color;
            //Debug.Log("myBannerColor = " + myBannerColor);
        }


        private void OnDisable()
        {
            m_InteractiveItem.OnOver -= HandleOver;
            m_InteractiveItem.OnOut -= HandleOut;
            m_InteractiveItem.OnClick -= HandleClick;
            m_InteractiveItem.OnDoubleClick -= HandleDoubleClick;
        }


        //Handle the Over event
        private void HandleOver()
        {
            Debug.Log("Show over state. myPlaneName = " + myPlaneName);
            // FF2C0F82
            //m_Renderer.material = m_OverMaterial;
            myBannerImg.color = new Color(1.0f, 0.173f, 0.059f, 0.510f);
        }


        //Handle the Out event
        private void HandleOut()
        {
            Debug.Log("Show out state");
            //m_Renderer.material = m_NormalMaterial;
            myBannerImg.color = myBannerColor;
        }


        //Handle the Click event
        private void HandleClick()
        {
            Debug.Log("Show click state");
            //m_Renderer.material = m_ClickedMaterial;
            myFlightRadarScript.myFuncShowBanner2(myPlaneName);
        }


        //Handle the DoubleClick event
        private void HandleDoubleClick()
        {
            Debug.Log("Show double click");
            //m_Renderer.material = m_DoubleClickedMaterial;
        }
    }

}