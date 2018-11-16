
using System;
using UnityEngine;
using UnityEngine.UI;


public class sMortarMovement : MonoBehaviour
{
    [SerializeField]
    float _panSpeed = 5f;

    [SerializeField]
    float _vertSpeed = 2f;

    [SerializeField]
    float _yawSpeed = 1f;

    Camera _referenceCamera;

    [SerializeField]
    float hMin = 100f;

    [SerializeField]
    float hMax = 20000f;

    [SerializeField]
    Text mySceenMessage;


    Quaternion _originalRotation;
    Vector3 _origin;
    Vector3 _delta;
    bool _shouldDrag;
    bool _shouldRotate; // флаг вращения ступы по курсу при нажатии правой кнопки мыши
    float myOldMouseX;

    // Параметры перелета
    
    // Положение в начале перелета
    Vector3 myStartPos;
    Vector3 myStarttEu;
    // Положение в конце перелета
    Vector3 myEndPos;
    Vector3 myEndEu;
    // Флаг перелета, блокирует управление
    bool myFlight = false;
    // Время начала перелета, сек
    float myStartTime;
    // Продолжительность перелета, сек
    [SerializeField]
    float myFlightTime = 2.0f;
    // Положение в начале сеанса
    Vector3 myHomePos;
    Vector3 myHomeEu;
    // Положение "на вышке"
    Vector3 myTowerPos = new Vector3 ( 280, 100, 1100 );
    Vector3 myTowerEu = new Vector3(0, 165, 0);
    // Положение "на хвосте" - локальный сдвиг относительно самолета-носителя
    Vector3 myTailPos = new Vector3(225, 300, -650);

    void Awake()
    {
        _referenceCamera = Camera.main;
        _originalRotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);

        if (_referenceCamera == null)
        {
            _referenceCamera = GetComponent<Camera>();
            if (_referenceCamera == null)
            {
                throw new System.Exception("You must have a reference camera assigned!");
            }
        }
        myHomePos = transform.position;
        myHomeEu = transform.eulerAngles;

    }

    void LateUpdate()
	{
		var x = 0f;
		var y = 0f;
		var z = 0f;
        var w = 0f;
        float myCurMouseX = 0;

        if (myFlight) // Только перелетаем в заданное положение
        {
            float myInterpolant = (Time.time - myStartTime) / myFlightTime;
            transform.localPosition = Vector3.Lerp(myStartPos, myEndPos, myInterpolant);
            //transform.localEulerAngles = Vector3.Lerp(myStarttEu, myEndEu, myInterpolant);
            transform.localRotation = Quaternion.Lerp(Quaternion.Euler(myStarttEu), Quaternion.Euler(myEndEu), myInterpolant);
            if(myInterpolant > 1)
            {
                myFlight = false;
            }
        }
        else // Все остальное управление
        {
            // Команда на перелет домой
            if (Input.GetKeyDown("h"))
            {
                transform.parent = null; // Выйти в корень иерархии сцены
                myStartTime = Time.time;
                myStartPos = transform.position;
                myStarttEu = transform.eulerAngles;
                myEndPos = myHomePos;
                myEndEu = myHomeEu;

                myFlight = true;
            }
            // Команда на перелет к башне
            else if (Input.GetKeyDown("t"))
            {
                transform.parent = null; // Выйти в корень иерархии сцены
                myStartTime = Time.time;
                myStartPos = transform.position;
                myStarttEu = transform.eulerAngles;
                myEndPos = myTowerPos;
                myEndEu = myTowerEu;

                myFlight = true;
            }
            // Команда сесть на хвост
            else if (Input.GetKeyDown("p"))
            {
                if (!transform.parent) // Если находимся в корне иерархии сцены
                {

                    // Найти ближайший самолет
                    Transform myPlanesControllerTr = GameObject.Find("PlanesController").transform; // родительский объект всех активных самолетов
                    int myPlanesCount = myPlanesControllerTr.childCount; // количество активных самолетов
                    if (myPlanesCount > 0) // продолжаем, только если есть активные самолеты
                    {
                        int myNearestPlaneNumber = 0; // Индекс ближайшего самолета среди детей myPlanesControllerTr
                        float myNearestPlaneDist = 1000000f; // расстояние от ступы до самолета
                        for (int i = 0; i < myPlanesCount; i++)
                        {
                            float myDist = Vector3.Distance(myPlanesControllerTr.GetChild(i).position, transform.position);
                            if (myDist < myNearestPlaneDist)
                            {
                                myNearestPlaneNumber = i;
                                myNearestPlaneDist = myDist;
                            }
                        }
                        // Перейти в дети ближайшего самолета
                        transform.parent = myPlanesControllerTr.GetChild(myNearestPlaneNumber);
                        // Перелететь "на хвост" ближайшего самолета
                        myStartPos = transform.localPosition;
                        myStarttEu = transform.localEulerAngles;
                        myStarttEu.y = myFuncNormalizeAngle(myStarttEu.y); // нормализовать курсовой угол в диапазоне +/- 180 градусов
                        myEndPos = myTailPos;
                        myEndEu = Vector3.zero;
                        myStartTime = Time.time;

                        myFlight = true;
                    }
                }
                else // Вернуться в корень (слезть с хвоста)
                {
                    transform.parent = null; // Выйти в корень иерархии сцены
                }
            }

            if (Input.GetMouseButton(0))
            {
                var mousePosition = Input.mousePosition;
                mousePosition.z = transform.localPosition.y;
                _delta = _referenceCamera.ScreenToWorldPoint(mousePosition) - transform.localPosition;
                _delta.y = 0f;
                if (_shouldDrag == false)
                {
                    _shouldDrag = true;
                    _origin = _referenceCamera.ScreenToWorldPoint(mousePosition);
                }
            }
            else
            {
                _shouldDrag = false;
            }

            if (Input.GetMouseButton(1))
            {
                myCurMouseX = Input.mousePosition.x;
                if (_shouldRotate == false)
                {
                    _shouldRotate = true;
                    myOldMouseX = myCurMouseX;
                }
            }
            else
            {
                _shouldRotate = false;
            }


            if (_shouldDrag == true) // Перемещать ступу
            {
                var offset = _origin - _delta;
                offset.y = transform.localPosition.y;
                transform.localPosition = offset;
            }
            else if (_shouldRotate == true) // Вращать ступу по курсу
            {
                if (myCurMouseX > myOldMouseX)
                {
                    w = 1.0f;
                }
                else if (myCurMouseX < myOldMouseX)
                {
                    w = -1.0f;
                }
                else if (myCurMouseX == myOldMouseX)
                {
                    w = 0.0f;
                }
                Vector3 myEu = transform.localEulerAngles; // Запомнить углы Эйлера
                myEu.y += w / 2.0f; // Повернуть по курсу
                transform.localEulerAngles = myEu; // Применить
            }
            else
            {
                x = Input.GetAxis("Horizontal");
                z = Input.GetAxis("Vertical");
                y = Input.GetAxis("Mouse ScrollWheel") * 20;
                if (y == 0f)
                {
                    y = Input.GetAxis("Throttle");
                }
                w = Input.GetAxis("Twist");
                float myCameraPitch = Input.GetAxis("Hat_Vert");

                //transform.localPosition += transform.forward * y + (_originalRotation * new Vector3(x * _panSpeed, 0, z * _panSpeed));

                // Повернуть по курсу и временно выровнять по тангажу
                Vector3 myEu = transform.localEulerAngles; // Запомнить углы Эйлера
                                                           //float myPitch = myEu.x; // Запомнить отдельно угол тангажа
                myEu.y += w * _yawSpeed; // Повернуть по курсу
                                         //myEu.x = 0.0f; // Выровнять по тангажу
                transform.localEulerAngles = myEu; // Применить

                // Позиционировать
                float myHorSpeed = Mathf.Clamp(_panSpeed * transform.localPosition.y / hMin, 10.0f, 1000.0f); // Умножим скорость перемещения на относительную высоту
                float myVertSpeed = Mathf.Clamp(_vertSpeed * transform.localPosition.y / hMin * transform.localPosition.y / hMin, _vertSpeed, _vertSpeed * 25.0f); // Умножим вертикальную скорость перемещения на относительную высоту
                transform.Translate(x * myHorSpeed, 0f, z * myHorSpeed); // Переместить по горизонтали
                Vector3 myPos = transform.localPosition; // Запомнить позицию
                myPos.y = Mathf.Clamp((myPos.y + y * y * y * myVertSpeed), hMin, hMax); // Установить новую высоту
                transform.localPosition = myPos; //Применить

                // Восстановить угол тангажа
                //myEu.x = myPitch;
                //transform.localEulerAngles = myEu;

                // Наклонить камеру по тангажу
                Vector3 myCamEu = _referenceCamera.transform.localEulerAngles;
                myCamEu.x = myCamEu.x + myCameraPitch;
                _referenceCamera.transform.localEulerAngles = myCamEu;
            }
        }
        mySceenMessage.text = "Высота камеры = " + Math.Round(transform.position.y, 2);
    }


    // Приведем угол от (0/360) к (-180/+180)
    float myFuncNormalizeAngle(float myAngle)
    {
        while (myAngle > 180.0f)
        {
            myAngle -= 360.0f;
        }
        while (myAngle < -180.0f)
        {
            myAngle += 360.0f;
        }
        return myAngle;
    }

}
