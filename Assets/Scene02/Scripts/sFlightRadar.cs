using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using Newtonsoft.Json.Linq;
using Mapbox.Unity.Utilities;
using Mapbox.Utils;

// Выполняет запрос к серверу adsbexchange.com, получает, обрабатывает и сохраняет данные полетов в массивы структур.


public class sFlightRadar : MonoBehaviour {

    // Корневая часть запроса к ADS-B Exchange
    String REQUEST_BASE_URL = "http://public-api.adsbexchange.com/VirtualRadar/AircraftList.json?";
    // Полный текст запроса к серверу
    String myURL;

    // Координаты начальной точки
    //[SerializeField]
    //String myStartLatitude = "55.97239722";
    //[SerializeField]
    //String myStartLongitude = "37.41305278";

    // Высота аэропорта над уровнем моря
    [SerializeField]
    float myAirport_ALt = 192;
    // Расстояние от начальной точки (км), на котором следить за самолетами
    [SerializeField]
    String myDistance = "60";
    // Один фут, метров
    [SerializeField]
    float myFeet = 0.3048f;
    // Узел, метров в секунду (1.852 км/час)
    [SerializeField]
    float myKnot = 0.514f;
    // Метрическая система, используемая по умолчанию (true - СИ, false - Футы/мили)
    [SerializeField]
    bool mySI = true;
    // Отставание отображения от жизни, миллисек. (самолет перемещается так, чтобы оказаться в последней точке через данное время
    [SerializeField]
    int myLag = 5000;
    // Желательное время цикла обработки данных, сек.
    [SerializeField]
    float myCycleTime = 2.0f;
    // Текстовый объект для вывода сообщений на экран
    [SerializeField]
    Text mySceenMessage;
    // Баннер № 2 для вывода доп. информации о самолете
    [SerializeField]
    Transform myBanner2;
    // Контроллер карты MapBox. Для работы с высотой в Иннсбруке
    [SerializeField]
    GameObject myMap;
    // Квадрат минимальной допустимой скорости движения самолета. Базовая величина соответствует 20 м/сек = 72 км/час (39,45 узла)
    [SerializeField]
    float myLowSpeedSqr = 400.0f;
    // Отладочный параметр - запиcывать ли логи
    [SerializeField]
    bool myWriteLog = true;

    // Объект для вывода сообщений в пространство
    sTextMessage myWorldMessage;

    // Массив объектов - пороги ВПП
    //[SerializeField]
    //GameObject[] myTHR = new GameObject[4];

    // Трансформ шаблона самолетов
    Transform mySamplePlane;
    // Трансформ группового объекта самолетов
    Transform myPlanesController;

    // Данные, которые будет определять MapBox (исправления в коде Sc02MapAtWorldScaleAndSpecificLocation.cs)
    [HideInInspector]
    public float myWorldRelativeScale;
    [HideInInspector]
    public Vector2d myCenterMercator;
    [HideInInspector]
    public Vector3 myPosShift;
    // Координаты начальной точки
    [HideInInspector]
    public String myStartLatitude;
    [HideInInspector]
    public String myStartLongitude;


    // Точное время
    Stopwatch myStopWatch;
    long myStartTime;
    int myCurrentTime;
    //int myDeltaTime;

    long myResponseTime = 0; // Время прихода новых данных от сервера
    long myStartProcTime = 0; // Время начала первичной обработки данных (если новые данные поступили, то совпадает с myResponseTime)
    long myLastDeltaTime = 0; // Время выполнения последнего полного цикла обработки данных (первичной + вторичной)

    // Текстовый объект для приема данных от сервера
    String myResponseStr = "";

    // Поток, в котором будем обрабатывать полученные данные и поддерживать структуры
    Thread myFightDataThread;

    // Флаги разных состояний
    bool myNewWebData = false; // Имеются новые необработанные данные
    bool myPrimaryDataProc = true; // Выполняется первичная обработка данных (в фоновом потоке), вторичную обработку не начинать!
    bool mySecondaryDataProc = false; // Выполняется вторичная обработка данных (в корутине), первичную обработку не начинать!
    bool myBanner1AddInfo = false; // Выводить ли на баннер с краткой информацией дополнительную информацию

    // ********************** Запись отладочных данных в файлы ********************************************

    String myRecDir = "Record";
    // Словарь - массив файлов для записи отладочных данных
    Dictionary<String, StreamWriter> myRecFile = new Dictionary<String, StreamWriter>();

    // ******************************************************************


    // Структура (на самом деле класс) для текущих параметров самолета, полученных от adsbexchange.com ("Большая")
    // Пришлось переделать в класс, так как экземпляры System.Reflection.FieldInfo не отрабатывают метод setValue для структур.
    class myPlaneParameters
    {
        // Список полей отсюда: http://www.virtualradarserver.co.uk/Documentation/Formats/AircraftList.aspx
        // Дополнительная информация: https://www.adsbexchange.com/datafields/

        public int Id; // The Unique Identifier of the aircraft (in current tracking session?). Virtual Radar Server
        public int TSecs; // The number of seconds that the aircraft has been tracked for. Virtual Radar Server
        public int Rcvr; // The ID of the feed that last supplied information about the aircraft. Virtual Radar Server
        public String Icao; // The ICAO hex identifier of the aircraft. Broadcast
        public bool Bad; // True if the ICAO is known to be invalid. This information comes from the local BaseStation.sqb database.
        public String Reg; // The aircraft registration number
        public DateTime FSeen; // Date and time the receiver first started seeing the aircraft on this flight (datetime – epoch format).
        public int Alt; // The altitude in feet at standard pressure. Broadcast
        public int GAlt; // The altitude adjusted for local air pressure, should be roughly the height above mean sea level.
        public float InHg; // The air pressure in inches of mercury that was used to calculate the AMSL altitude from the standard pressure altitude.
        public int AltT; // The type of altitude transmitted by the aircraft: 0= standard pressure altitude, 1= indicated altitude (above mean sea level). Default to standard pressure altitude until told otherwise.
        public int TAlt; // The target altitude, in feet, set on the autopilot / FMS etc. Broadcast
        public String Call; // The callsign. Broadcast
        public bool CallSus; // True if the callsign may not be correct. Based on a checksum of the data received over the air.
        public float Lat; // The aircraft’s latitude over the ground. Broadcast
        public float Long; // The aircraft’s longitude over the ground. Broadcast
        public long PosTime; // The time (at UTC in JavaScript ticks) that the position was last reported by the aircraft.
        public bool Mlat; // True if the latitude and longitude appear to have been calculated by an MLAT server and were not transmitted by the aircraft.
        public bool PosStale; // True if the last position update is older than the display timeout value - usually only seen on MLAT aircraft in merged feeds. Internal field, basically means that the position data is > 60 seconds old (unless it’s from Satellite ACARS).
        public bool Tisb; // True if the last message received for the aircraft was from a TIS-B source.
        public float Spd; // The ground speed in knots. Broadcast
        public int SpdTyp; // The type of speed that Spd represents. Only used with raw feeds. 0/missing= ground speed, 1= ground speed reversing, 2= indicated air speed, 3= true air speed.
        public int Vsi; // Vertical speed in feet per minute. Broadcast
        public int VsiT; // 0= vertical speed is barometric, 1= vertical speed is geometric. Default to barometric until told otherwise.
        public float Trak; // Aircraft's track angle across the ground clockwise from 0° north. Broadcast(?).
        public bool TrkH; // True if Trak is the aircraft's heading, false if it's the ground track. Default to ground track until told otherwise.
        public float TTrk; // The track or heading currently set on the aircraft's autopilot or FMS. Broadcast
        public String Type; // The aircraft model's ICAO type code.
        public String Mdl; // A description of the aircraft's model.
        public String Man; // The manufacturer's name.
        public String CNum; // The aircraft's construction or serial number.
        public String From; // The code and name of the departure airport.
        public String To; // The code and name of the arrival airport.
        public String Stops; // An array of strings, each being a stopover on the route.
        public String Op; // The name of the aircraft's operator.
        public String OpIcao; // The operator's ICAO code.
        public String Sqk; // Transponder squawk code. This is a 4-digit code (each digit is from 0-7) entered by the pilot, and typically assigned by air traffic control. A sqwak code of 1200 typically means the aircraft is operation under VFR and not receiving radar services. 7500= Hijack code, 7600= Lost Communications, radio problem, 7700= Emergency.
        public bool Help; // True if the aircraft is transmitting an emergency squawk.
        public float Dst; // The distance to the aircraft in kilometres.
        public float Brng; // The bearing from the browser to the aircraft clockwise from 0° north.
        public int WTC; // The wake turbulence category (0= None, 1= Light, 2= Medium, 3= Heavy). Broadcast
        public String Engines; // The number of engines the aircraft has. Usually '1', '2' etc. but can also be a string - see ICAO documentation.
        public int EngType; // Type of engine the aircraft uses (0= None, 1= Piston, 2= Turboprop, 3= Jet, 4= Electric).
        public int EngMount; // The placement of engines on the aircraft (0= Unknown, 1= Aft Mounted, 2= Wing Buried, 3= Fuselage Buried, 4 =Nose Mounted, 5= Wing Mounted).
        public int Species; // General Aircraft Type (0 =None, 1 =Land Plane, 2= Sea Plane, 3= Amphibian, 4= Helicopter, 5= Gyrocopter, 6= Tiltwing, 7= Ground Vehicle, 8= Tower).
        public bool Mil; // True if the aircraft appears to be operated by the military.
        public String Cou; // The country that the aircraft is registered to.
        public bool HasPic; // True if the aircraft has a picture associated with it in the VRS/ADSBexchange database. Pictures often link to http://www.airport-data.com.
        public int PicX; // The width of the picture in pixels.
        public int PicY; // The height of the picture in pixels.
        public int FlightsCount; // The number of Flights records the aircraft has in the database.
        public int CMsgs; // The count of messages received for the aircraft. Will change as aircraft roams between receiving servers.
        public bool Gnd; // True if the aircraft is on the ground. Broadcast
        public String Tag; // The user tag found for the aircraft in the BaseStation.sqb local database.
        public bool Interested; // True if the aircraft is flagged as interesting in the BaseStation.sqb local database.
        public String TT; // Trail type - empty for plain trails, 'a' for trails that include altitude, 's' for trails that include speed.
        public int Trt; // Transponder type - 0=Unknown, 1=Mode-S, 2=ADS-B (unknown version), 3=ADS-B 0, 4=ADS-B 1, 5=ADS-B 2.
        public int Year; // The year that the aircraft was manufactured.
        public bool Sat; // True if the aircraft has been seen on a SatCom ACARS feed (e.g. a JAERO feed).
        public int[] Cos; // Short trails.
        public int[] Cot; // Full trails.
        public bool ResetTrail; // True if the entire trail has been sent and the JavaScript should discard any existing trail history it's built up for the aircraft.
        public bool HasSig; // True if the aircraft has a signal level associated with it.
        public int Sig; // The signal level for the last message received from the aircraft, as reported by the receiver. Not all receivers pass signal levels. The value's units are receiver-dependent.
    }

    // Словарь - массив параметров всех самолетов, полученных от adsbexchange. Ключ - HEX код ICAO (Icao), значение - структура со всеми данными
    // Если поле Icao пустое, используем для ключа ID, выдаваемый виртуальным радар-сервером
    Dictionary<String, myPlaneParameters> myAllPlanesPars = new Dictionary<String, myPlaneParameters>();
    // Коллекция ключей словаря
    Dictionary<String, myPlaneParameters>.KeyCollection myAllPlanesKeysList;

    // Словарь - массив для описания типов полей большой структуры. Ключ - имя поля, значение - тип поля
    Dictionary<String, Type> myPlaneParsType = new Dictionary<String, Type>();

    
    // Структура для отображения самолета ("Малая")
    struct MyPlaneVisual
    {
        public String Key;
        public String Call;
        public String Icao;
        public String Reg;
        public String Alt;
        public GameObject GO;
        public Transform Banner1;
        public Text Banner1Call;
        public Text Banner1Icao;
        public Text Banner1PReason;
        public Text Banner1Model;
        public Text Banner1Alt;
        public Image Banner1Panel;
        public Transform Model;
        public String PredictionReason;
        public Vector3 Position;
        public Vector3 Euler;
        public Vector3 Speed;
        public long Time;
    }

    // Словарь - данные для отображения самолетов. Ключ тот же, значения - структура
    Dictionary<String, MyPlaneVisual> myPlaneVis = new Dictionary<String, MyPlaneVisual>();
    // Коллекция значений словаря
    Dictionary<String, MyPlaneVisual>.ValueCollection myPlaneVisValues;
    // Коллекция ключей словаря
    Dictionary<String, MyPlaneVisual>.KeyCollection myPlaneVisKeys;

    // Структура для истории полетов ("История")
    struct MyFlightHistory
    {
        public List<long> Time;
        public List<long> PosTime;
        public List<String> PredictionReason;
        public List<Vector3> Position;
        public List<Vector3> Euler;
        public List<Vector3> Speed;
    }
    // Словарь - истории полетов. Ключ тот же, значения - структура массивов List с временем, координатами и скоростями
    Dictionary<String, MyFlightHistory> myPlanesHistory = new Dictionary<String, MyFlightHistory>();

    // Словарь - список севших самолетов. Нужен, чтобы не создавать такой заново, если он уже удален, а данные еще поступают
    Dictionary<String, long> myLandedPlanes = new Dictionary<String, long>();

    // Словарь - список самолетов со слишком малой скоростью. Нужен, чтобы не отображать "зависшие" самолеты
    Dictionary<String, bool> mySlowPlanes = new Dictionary<String, bool>();
    // Счетчик медленных самолетов
    int mySlowPlanesCount = 0;

    // Словарь - список имеющихся моделей самолетов. Содержит указатели на модели. Ключи - имена на сцене. Нужен для быстрого доступа к моделям при создании новых самолетов
    Dictionary<String, Transform> myPlanes3D = new Dictionary<String, Transform>();

    // Словарь "Известные модели самолетов -> имеющиеся 3D модели". Ключ - "Код ИКАО модели", содержание - первая часть имени модели на сцене (без авиакомпании)
    Dictionary<String, String> myKnownPlanes = new Dictionary<String, String>();

    // Словарь "Известные авиакомпании -> имеющиеся авиакомпании (среди 3D моделей)". Ключ - "Код ИКАО авиакомпании", содержание - вторая часть имени модели на сцене (без модели самолета)
    Dictionary<String, String> myKnownAirlines = new Dictionary<String, String>();

    // Словарь - список полей баннера с дополнительной информацией
    Dictionary<String, Text> myBanner2Fields = new Dictionary<String, Text>();

    // Самолет, выбранный для отображения дополнительной информации (ключ - HEX код ICAO или ID от Virtual Radar Server)
    String mySelectedPlane = null;

    // Use this for initialization
    void Start () {

        // ********************** Запись в файл отладочных данных ********************************************

        // Подготовим файл для записи

        // Создать папку
        Directory.CreateDirectory(myRecDir);
        myRecDir = Path.Combine(Directory.GetCurrentDirectory(), myRecDir);

        // Файл для записи по умолчанию
        String myRecFileName = "Main";
        myRecFile.Add(myRecFileName, new StreamWriter(Path.Combine(myRecDir, myRecFileName + ".txt")));
        // Файл для записи получаемых данных
        myRecFileName = "RawData";
        myRecFile.Add(myRecFileName, new StreamWriter(Path.Combine(myRecDir, myRecFileName + ".txt")));
        // Файл для записи в фоновом потоке
        myRecFileName = "Thread";
        myRecFile.Add(myRecFileName, new StreamWriter(Path.Combine(myRecDir, myRecFileName + ".txt")));
        // Файл для записи в процессе обработки данных (комбинированный поток: фоновый + корутина)
        myRecFileName = "ProcData";
        myRecFile.Add(myRecFileName, new StreamWriter(Path.Combine(myRecDir, myRecFileName + ".txt")));
        // Файл для записи в каждом кадре
        myRecFileName = "Update";
        myRecFile.Add(myRecFileName, new StreamWriter(Path.Combine(myRecDir, myRecFileName + ".txt")));

        // ******************************************************************

        // Отладка. Проверка времени
        myStopWatch = new Stopwatch();
        myStopWatch.Start();
        myStartTime = myStopWatch.ElapsedMilliseconds;
        myCurrentTime = 0;
        //myDeltaTime = 0;

        // Полный текст запроса к серверу
        myURL = REQUEST_BASE_URL + "lat=" + myStartLatitude + "&lng=" + myStartLongitude + "&fDstL=0&fDstU=" + myDistance;

        // Трансформ шаблона самолетов - получить указатель и сразу спрятать
        mySamplePlane = GameObject.Find("SamplePlane").transform;
        mySamplePlane.gameObject.SetActive(false);

        // Трансформ группового объекта самолетов
        myPlanesController = GameObject.Find("PlanesController").transform;

        // Коллекция значений словаря
        myPlaneVisValues = myPlaneVis.Values; // малый
        // Коллекция ключей словарей
        myPlaneVisKeys = myPlaneVis.Keys; // малый

        // Заполним словарь - список имеющихся моделей самолетов
        Transform myObjTr = GameObject.Find("Planes3D").transform;
        for(int i=0; i < myObjTr.childCount; i++)
        {
            Transform myPlaneTr = myObjTr.GetChild(i);
            myPlanes3D.Add(myPlaneTr.name, myPlaneTr);
        }

        // Заполним словарь "Известные модели самолетов -> имеющиеся 3D модели". Ключ - "Код ИКАО модели", содержание - первая часть имени модели на сцене (без авиакомпании)
        myKnownPlanes.Add("A319", "A320");
        myKnownPlanes.Add("A320", "A320");
        myKnownPlanes.Add("A321", "A320");

        myKnownPlanes.Add("A332", "A330");
        myKnownPlanes.Add("A333", "A330");

        myKnownPlanes.Add("A388", "A380");

        myKnownPlanes.Add("B731", "B737");
        myKnownPlanes.Add("B732", "B737");
        myKnownPlanes.Add("B733", "B737");
        myKnownPlanes.Add("B734", "B737");
        myKnownPlanes.Add("B735", "B737");
        myKnownPlanes.Add("B736", "B737");
        myKnownPlanes.Add("B737", "B737");
        myKnownPlanes.Add("B738", "B737");
        myKnownPlanes.Add("B739", "B737");

        myKnownPlanes.Add("B741", "B747");
        myKnownPlanes.Add("B742", "B747");
        myKnownPlanes.Add("B743", "B747");
        myKnownPlanes.Add("B744", "B747");
        myKnownPlanes.Add("B748", "B747");
        myKnownPlanes.Add("B74D", "B747");
        myKnownPlanes.Add("B74R", "B747");
        myKnownPlanes.Add("B74S", "B747");

        myKnownPlanes.Add("B772", "B777");
        myKnownPlanes.Add("B773", "B777");

        myKnownPlanes.Add("E135", "ERJ145");
        myKnownPlanes.Add("E145", "ERJ145");

        myKnownPlanes.Add("E170", "E170");
        myKnownPlanes.Add("IL96", "IL96");
        myKnownPlanes.Add("SU95", "SSJ100");

        myKnownPlanes.Add("C172", "Cessna172");
        myKnownPlanes.Add("C72R", "Cessna172");

        // Заполним словарь "Известные авиакомпании -> имеющиеся авиакомпании". Ключ - "Код ИКАО авиакомпании", содержание - вторая часть имени модели на сцене (без модели самолета)
        myKnownAirlines.Add("AFL", "AFL");
        myKnownAirlines.Add("SBI", "S7");


        // Заполним словарь - список полей баннера с дополнительной информацией
        // Для всех детей объекта myBanner2
        for (int i = 0; i < myBanner2.childCount; i++)
        {
            Transform myChildTr = myBanner2.GetChild(i);
            String myChildName = myChildTr.name;
            if(myChildName != "Panel") // Исключение: объект "Panel" - это не текстовое поле
            {
                myBanner2Fields.Add(myChildName, myChildTr.GetComponent<Text>());
                print(myChildName + " = " + myBanner2Fields[myChildName]);
            }
        }


        // Словарь для описания типов полей большой структуры. Ключ - имя поля, значение - тип поля

        myPlaneParameters myPP = new myPlaneParameters(); // Создадим пустой экземпляр большой структуры. Тестируя содержимое его полей узнаем их типы
        Type myPPType = typeof(myPlaneParameters); // тип объекта "myPlaneParameters"
        System.Reflection.MemberInfo[] memberlist = myPPType.GetMembers(); // Получим список членов данного типа

        // Пройдем по всем полям большой структуры
        for (int i = 0; i < memberlist.Length; i++)
        {
            // Только для членов, которые являются полями
            if (memberlist[i].MemberType == System.Reflection.MemberTypes.Field)
            {
                String myName = memberlist[i].Name; // имя поля
                System.Reflection.FieldInfo myFieldInfo = myPPType.GetField(myName); // метаинформация поля
                Type myType; // тип поля
                // Попробуем получить тип из содержимого поля
                try
                {
                    myType = myFieldInfo.GetValue(myPP).GetType();
                }
                catch // В пустом экземпляре строки и массивы не присвоены и выдают ошибку
                {
                    //MyLog(myCount + ", Ошибка: " + myEx.Message + ", Name = " + myName);
                    if(myName == "Cos" || myName == "Cot") // известно, что эти поля - массивы int
                    {
                        myType = typeof(int[]);
                    }
                    else // все остальные поля - String
                    {
                        myType = typeof(String);
                    }
                }
                myPlaneParsType.Add(myName, myType);
            }
        }

        // Объект для вывода сообщений в пространство
        myWorldMessage = GameObject.Find("TextMessage").GetComponent<sTextMessage>();

        // Спрячем все имеющиеся образцовые модели
        myObjTr.gameObject.SetActive(false);

        // Запуск корутины получения данных от сервера
        StartCoroutine(myFuncGetData());

        // Запуск фонового потока первичной обработки данных
        myFightDataThread = new Thread(new ThreadStart(myFuncThread)) { IsBackground = true }; // Создаем поток из функции myFuncThread()

        // Объявляем о начале первичной обработки данных в потоке (На самом деле, сначала поток еще будет ждать первого получения данных). Нужно для блокировки вторичной обработки
        myPrimaryDataProc = true;
        mySecondaryDataProc = false;

        // Запускаем поток
        myFightDataThread.Start();

        // Запуск корутины вторичной обработки данных
        StartCoroutine(myFuncProcData());

        // Для работы с высотой в Иннсбруке
        if(myAirport_ALt == 581.0f)
        {
            StartCoroutine(myFuncInnsbrukSpecial());
        }
    }

    IEnumerator myFuncInnsbrukSpecial()
    {
        yield return new WaitForEndOfFrame();
        Vector3 myPos = myMap.transform.position;
        myPos.y = -myAirport_ALt;
        myMap.transform.position = myPos;
    }


    // 4 перегруженных функции для записи лог-файлов
    // Запись в указанный файл
    void MyLog(string myRecName, String myInfo)
    {
        if (myWriteLog)
        {
            myCurrentTime = (int)(myStopWatch.ElapsedMilliseconds - myStartTime);
            myRecFile[myRecName].WriteLine(myInfo + " CurrentTime = " + myCurrentTime);
        }
    }

    // Запись в указанный файл с возможностью не добавлять время
    void MyLog(string myRecName, String myInfo, bool myTime)
    {
        if (myWriteLog)
        {
            if (myTime)
            {
                myCurrentTime = (int)(myStopWatch.ElapsedMilliseconds - myStartTime);
                myRecFile[myRecName].WriteLine(myInfo + " CurrentTime = " + myCurrentTime);
            }
            else
            {
                myRecFile[myRecName].WriteLine(myInfo);
            }
        }
    }

    // Запись в файл по умолчанию
    void MyLog(String myInfo)
    {
        if (myWriteLog)
        {
            myRecFile["Main"].WriteLine(myInfo);
        }
    }

    // Запись в два файла
    void MyLog(string myRecName1, string myRecName2, String myInfo)
    {
        if (myWriteLog)
        {
            myCurrentTime = (int)(myStopWatch.ElapsedMilliseconds - myStartTime);
            myRecFile[myRecName1].WriteLine(myInfo + " CurrentTime = " + myCurrentTime);
            myRecFile[myRecName2].WriteLine(myInfo + " CurrentTime = " + myCurrentTime);
        }
    }

    // Запросить в Интернете, получить, и записать полетные данные в текстовую строку
    IEnumerator myFuncGetData()
    {
        long myWebRequestTime = 0;
        int myWebRequestCount = 0;
        int myDataTraffic = 0;

        MyLog("RawData", "@@@ myFuncGetData(): Начну выполнять запросы через ~ 1 секунду");
        yield return new WaitForSeconds(1);
        MyLog("RawData", "@@@ myFuncGetData(): Подождали 1 секунду, начинаем");

        while (true)
        {
            myWebRequestTime = myStopWatch.ElapsedMilliseconds - myStartTime;
            MyLog("RawData", "@@@ myFuncGetData(): Начинаю запрос. Время = " + myWebRequestTime + " myURL = " + myURL);

            // Готовим запрос
            UnityWebRequest myRequest = UnityWebRequest.Get(myURL);
            // Выполняем запрос и получаем ответ
            yield return myRequest.SendWebRequest();
            // Зафиксируем время ответа и интервал времени от предыдущего ответа
            myResponseTime = myStopWatch.ElapsedMilliseconds - myStartTime; // Время получения данных от сервера
            myWebRequestTime = myResponseTime - myWebRequestTime; // Время, которое выполняли запрос и получали ответ
            myWebRequestCount++; // Номер запроса

            if (myRequest.isNetworkError || myRequest.isHttpError)
            {
                MyLog("RawData", "@@@ myFuncGetData(): Запрос не выполнен. Номер запроса = " + myWebRequestCount + " Время на запрос/ответ = " + myWebRequestTime);
                MyLog("RawData", "@@@ myFuncGetData(): Ошибка " + myRequest.error + " Продолжу работать через ~3 секунды");
                yield return new WaitForSeconds(3);
            }
            else
            {
                // Results as text
                myResponseStr = myRequest.downloadHandler.text;
                // Установим флаг "Имеются новые необработанные данные"
                myNewWebData = true;
                // Отчитаемся о результатах запроса
                myDataTraffic += myResponseStr.Length;
                MyLog("RawData", "@@@ myFuncGetData(): Запрос выполнен. myNewWebData = " + myNewWebData + " Номер запроса = " + myWebRequestCount + " Время прихода ответа = " + myResponseTime + " Время на запрос/ответ = " + myWebRequestTime + " Получена строка длиной = " + myResponseStr.Length + " Общий траффик авиаданных = " + myDataTraffic);
                //MyLog("RawData", "@@@ myFuncGetData(): " + myResponseStr);
            }

            myRequest.Dispose(); // завершить запрос, освободить ресурсы

            // Переждать до конца рекомендованного времени цикла, секунд (если запрос занял времени меньше)
            float myWaitTime = Mathf.Max(0.0f, (myCycleTime - myWebRequestTime / 1000.0f));
            MyLog("RawData", "@@@ myFuncGetData(): Переждем до следующего запроса секунд: " + myWaitTime + ".");
            yield return new WaitForSeconds(myWaitTime);
            MyLog("RawData", "@@@ myFuncGetData(): Переждали еще секунд: " + myWaitTime + " Буду делать следующий запрос");
        }

    }


    // Первичная обработка данных, выполняемая в фоновом потоке. Сюда, по возможности, вынесены операции из корутины myFuncProcData()
    void myFuncThread()
    {

        // Ждем получения новых данных в корутине myFuncGetData(). Выполняется только в начале работы программы
        MyLog("RawData", "ProcData", "=== myFuncThread(): Ждем первого получения новых данных. myNewRawData = " + myNewWebData);
        while (!myNewWebData)
        {
            Thread.Sleep(200);
        }
        bool myFirstData = true; // Признак первого получения данных

        // Бесконечный цикл первичной обработки данных
        while (true)
        {
            if (myFirstData)
            {
                MyLog("RawData", "ProcData", "=== myFuncThread(): Обнаружено первое получение новых данных. myNewRawData = " + myNewWebData);
                myFirstData = false;
            }
            else
            {
                // Ждем заверешения вторичной обработки данных в корутине myFuncProcData()
                MyLog("RawData", "ProcData", "**********************************************************************");
                MyLog("RawData", "ProcData", "=== myFuncThread(): Ждем заверешения вторичной обработки данных в корутине myFuncProcData()");
                while (mySecondaryDataProc)
                {
                    Thread.Sleep(20);
                }
                MyLog("RawData", "ProcData", "=== myFuncThread(): Обнаружено, что вторичная обработка данных завершена.");
            }
            MyLog("RawData", "ProcData", "=== myFuncThread(): Начинаем первичную обработку данных");

            // Начинаем первичную обработку данных
            // Если получены новые данные в корутине myFuncGetData(), то начинаем их разбирать
            if (myNewWebData)
            {
                MyLog("RawData", "ProcData", "=== myFuncThread(): Есть новые данные от myFuncGetData(). myNewWebData = " + myNewWebData);
                myNewWebData = false;
                myLastDeltaTime = myResponseTime - myStartProcTime; // Время последнего выполненного полного цикла обработки данных
                myStartProcTime = myResponseTime; // Время начала нового цикла

                MyLog("RawData", "ProcData", "=== myFuncThread(): Время начала нового цикла обработки считаем от myResponseTime = myStartProcTime = " + myStartProcTime + " myLastDeltaTime = " + myLastDeltaTime);
                // Парсим полученную строку и создаем объект JSON
                dynamic myJObj = JObject.Parse(myResponseStr);
                // Узел acList (список самолетов) - массив
                JArray myAcList = myJObj.acList;

                MyLog("RawData", "ProcData", "=== myFuncThread(): Отпарсили строку в JSON. Количество самолетов в зоне по поступившим данным = " + myAcList.Count);

                // Переделаем объект JSON в структуру, добавим ее в словарный массив (или перепишем, если такая уже имеется)
                // Список полей отсюда: http://www.virtualradarserver.co.uk/Documentation/Formats/AircraftList.aspx
                // Дополнительная информация: https://www.adsbexchange.com/datafields/

                for (int i = 0; i < myAcList.Count; i++)
                {
                    // Создаем одиночные экземпляры структур
                    myPlaneParameters myOnePlanePars = new myPlaneParameters(); // Большая структура
                    MyPlaneVisual myPlane = new MyPlaneVisual(); // Малая структура
                    MyFlightHistory myOnePlaneHist = new MyFlightHistory(); // История самолета
                    String myKey; // Уникальный ключ самолета (код ICAO или ID, присвоенный VirtualRadarServer)

                    // *************************************************************************************************************
                    // Начинаем заполнять экземпляр структуры myOnePlanePars (Большой) из объекта JSON - основные данные
                    MyLog("ProcData", "=== myFuncThread(): " + i + " Создали структуры, начинаем заполнять большую");

                    myOnePlanePars.Id = myJObj.acList[i].Id; // The Unique Identifier of the aircraft (in current tracking session?). Virtual Radar Server.
                    myOnePlanePars.Icao = myJObj.acList[i].Icao; // The ICAO hex identifier of the aircraft. Broadcast.

                    // Установим ключ для записи параметров данного самолета в словарь
                    if (!String.IsNullOrEmpty(myOnePlanePars.Icao)) // Если код ICAO в порядке
                    {
                        myKey = myOnePlanePars.Icao; // Ключом будет код ICAO
                    }
                    else
                    {
                        myKey = myOnePlanePars.Id.ToString(); // Ключом будет код Virtual Radar Server
                    }
                    MyLog("ProcData", "=== myFuncThread(): " + i + " Установили ключ = " + myKey);
                    myOnePlanePars.Lat = myJObj.acList[i].Lat; // The aircraft’s latitude over the ground. Broadcast.
                    myOnePlanePars.Long = myJObj.acList[i].Long; // The aircraft’s longitude over the ground. Broadcast.
                    myOnePlanePars.CMsgs = myJObj.acList[i].CMsgs; // Номер нового сообщения от самолета. Сомнительное условие.

                    // Проверим: может быть данные, полученные для этого самолета, совпадают с предыдущими, уже записанными в словаре myAllPlanesPars
                    myPlaneParameters myOnePlanePreviosPars;
                    bool myPlaneIsNew = true;
                    bool myPlaneBackData = false;
                    bool myPlaneFrozen = false;
                    bool myPlaneBadPos = false;
                    MyLog("ProcData", "=== myFuncThread(): " + i + " ключ = " + myKey + " Попробуем найти с таким же ключом");
                    if (myAllPlanesPars.TryGetValue(myKey, out myOnePlanePreviosPars)) // Если есть самолет с таким ключом считаем его параметры в экземпляр большой структуры myOnePlanePreviosPars
                    {
                        MyLog("ProcData", "=== myFuncThread(): " + i + " ключ = " + myKey + " Да, самолет с таким ключом уже есть");
                        myPlaneIsNew = false;

                        if (myOnePlanePars.CMsgs <= myOnePlanePreviosPars.CMsgs) // Номер нового сообщения от самолета меньше или равен номеру предыдущего. Сомнительное условие. 
                        {
                            myPlaneBackData = true;
                            MyLog("ProcData", "=== myFuncThread(): " + i + " ключ = " + myKey + " Номер нового сообщения от самолета меньше или равен номеру предыдущего. Новый CMsgs = " + myOnePlanePars.CMsgs + ", старый = " + myOnePlanePreviosPars.CMsgs);
                        }
                        else if (myOnePlanePars.Lat == myOnePlanePreviosPars.Lat & myOnePlanePars.Long == myOnePlanePreviosPars.Long) // Новые и старые координаты совпадают
                        {
                            myPlaneFrozen = true;
                            MyLog("ProcData", "=== myFuncThread(): " + i + " ключ = " + myKey + " Новые и старые координаты совпадают");
                        }
                        else
                        {
                            MyLog("ProcData", "=== myFuncThread(): " + i + " ключ = " + myKey + " Новые и старые координаты не совпадают");
                        }
                    }
                    else // Возможно, появился новый самолет.
                    {
                        MyLog("ProcData", "=== myFuncThread(): " + i + " ключ = " + myKey + " Нет, самолета с таким ключом нет");
                        // Проверим, может это уже удаленный, который недавно сел
                        if (myLandedPlanes.ContainsKey(myKey)) // Самолет есть в списке севших
                        {
                            MyLog("ProcData", "=== myFuncThread(): " + i + " ключ = " + myKey + " Но самолет с таким ключом есть в списке севших");
                            if (myStartProcTime - 600000 < myLandedPlanes[myKey]) // Самолет сел менее 10 минут назад
                            {
                                MyLog("ProcData", "=== myFuncThread(): " + i + " ключ = " + myKey + " Самолет сел менее 10 минут назад. Ничего делать не будем, переходим к следующему самолету");
                                continue; // Ничего делать не будем, переходим к следующему самолету
                            }
                            MyLog("ProcData", "=== myFuncThread(): " + i + " ключ = " + myKey + " Самолет сел более 10 минут назад. Удаляем из списка севших, продолжаем обработку его данных");
                            myLandedPlanes.Remove(myKey); // Удаляем самолет из списка севших, продолжаем обработку его данных
                        }
                        try // Создадим лог-файлы для самолета
                        {
                            myRecFile.Add(myKey, new StreamWriter(Path.Combine(myRecDir, myKey + ".txt")));
                            myRecFile.Add(myKey+"_Data", new StreamWriter(Path.Combine(myRecDir, myKey + "_Data.txt")));
                        }
                        catch (Exception myEx)
                        {
                            MyLog("ProcData", "=== myFuncThread(): Создание лог-файла для самолета с ключом = " + myKey + " Ошибка: " + myEx.Message);
                        }
                    }

                    MyLog("ProcData", "=== myFuncThread(): " + i + " ключ = " + myKey + " New = " + myPlaneIsNew + " BackData = " + myPlaneBackData + " Frozen = " + myPlaneFrozen + " Далее продолжим заполнять большую структуру из объекта JSON");

                    // Продолжим заполнять большую структуру из объекта JSON
                    //myOnePlanePars = MyFuncBigStructure2(myOnePlanePars, myJObj, i, myKey);
                    myOnePlanePars = MyFuncBigStructure(myOnePlanePars, myJObj, i, myKey);
                    MyLog("ProcData", "=== myFuncThread(): " + i + " ключ = " + myKey + " Заполнили экземпляр большой структуры. Начнем заполнять малую");

                    // Разбор полученных данных JSON и заполнение основной структуры myOnePlanePars для текущего самолета закончены.
                    // *************************************************************************************************************
                    // Преобразуем данные и заполним экземпляр рабочей структуры myPlane (Малая)
                    // Поля GO, Banner1 и Banner1TextX заполняются позже, при создании или обновлении записей в словарях

                    myPlane.Key = myKey;
                    myPlane.Call = myOnePlanePars.Call; // Позывной
                    myPlane.Icao = myOnePlanePars.Icao; // HEX код ICAO (ADS-B Mode-S код)
                    myPlane.Reg = myOnePlanePars.Reg; // Регистрационный ID ICAO
                    myPlane.Time = myStartProcTime; // Время последней порции данных

                    // Высота в футах. На самом деле разборка должна быть более сложная, с учетом GAlt и AltT, а также, возможно, InHG
                    MyLog("ProcData", "=== myFuncThread(): " + i + " ключ = " + myKey + " myOnePlanePars.Gnd = " + myOnePlanePars.Gnd + " myOnePlanePars.GAlt = " + myOnePlanePars.GAlt);
                    if (myOnePlanePars.Gnd || myOnePlanePars.GAlt == 0)
                    {
                        myPlane.Alt = "Gnd";
                    }
                    else
                    {
                        myPlane.Alt = myOnePlanePars.GAlt.ToString();
                    }
                    MyLog("ProcData", "=== myFuncThread(): " + i + " ключ = " + myKey + " Высота = " + myPlane.Alt);

                    // Положение

                    // Готовим прогноз на новое положение
                    if (myPlanesHistory.TryGetValue(myKey, out myOnePlaneHist)) // история данного самолета
                    {
                        int k = myOnePlaneHist.Time.Count; // число записей в истории
                        Vector3 myLastPosition = myOnePlaneHist.Position[k - 1];
                        Vector3 myPredictedPosition = myLastPosition + myOnePlaneHist.Speed[k - 1] * myLastDeltaTime / 1000;
                        MyLog(myKey, "ProcData", "=== myFuncThread(): ключ = " + myKey + " Прогноз. LastDeltaTime = " + myLastDeltaTime + " Previous Position =" + myOnePlaneHist.Position[k - 1] + " Previous Speed = " + myOnePlaneHist.Speed[k - 1] + " Predicted Position = " + myPredictedPosition);

                        if (myPlaneBackData) // Если получен устаревший пакет данных, то "летим по прогнозу"
                        {
                            myPlane.Position = myPredictedPosition;
                            MyLog(myKey, "ProcData", "=== myFuncThread(): ключ = " + myKey + " Данные полета - устаревший пакет, летим по прогнозу. Новый CMsgs = " + myOnePlanePars.CMsgs + ", старый = " + myOnePlanePreviosPars.CMsgs + ", myPlane.Position = " + myPlane.Position);
                        }
                        else if (myPlaneFrozen) // Если данные полета заморожены, то "летим по прогнозу"
                        {
                            myPlane.Position = myPredictedPosition;
                            MyLog(myKey, "ProcData", "=== myFuncThread(): ключ = " + myKey + " Данные полета не изменились, летим по прогнозу. myPlane.Position = " + myPlane.Position);
                        }
                        else
                        {
                            // Преобразуем полученные данные (высоту в футах и географические координаты) в положение в пространстве сцены
                            Vector3 myResponsedPosition;
                            myResponsedPosition.y = Mathf.Max(myOnePlanePars.GAlt * myFeet - myAirport_ALt, 0.0f); // Высота
                            Vector2d worldPosition = Conversions.GeoToWorldPosition(myOnePlanePars.Lat, myOnePlanePars.Long, myCenterMercator, myWorldRelativeScale);
                            myResponsedPosition.x = (float)worldPosition.x;
                            myResponsedPosition.z = (float)worldPosition.y;
                            myResponsedPosition = myResponsedPosition + myPosShift;
                            MyLog(myKey, "ProcData", "=== myFuncThread(): ключ = " + myKey + " Полученные данные. Responsed Position = " + myResponsedPosition);

                            // Сравним прогноз и полученные данные
                            Vector3 myPredictedVector = myPredictedPosition - myLastPosition;
                            Vector3 myResponsedVector = myResponsedPosition - myLastPosition;
                            float myAngle = Vector3.Angle(myPredictedVector, myResponsedVector);
                            MyLog(myKey, "ProcData", "=== myFuncThread(): ключ = " + myKey + " Угол между направлением на прогноз и полученной точкой (град.) = " + myAngle);
                            if (myAngle > 170)
                            {
                                myPlaneBadPos = true;
                                myPlane.Position = myPredictedPosition;
                                MyLog(myKey, "ProcData", "=== myFuncThread(): ключ = " + myKey + " Данные полета считаем недостоверными, летим по прогнозу. myPlane.Position = " + myPlane.Position);
                            }
                            else
                            {
                                myPlane.Position = myResponsedPosition;
                                MyLog(myKey, "ProcData", "=== myFuncThread(): ключ = " + myKey + " Данные полета считаем достоверными, летим по ним. myPlane.Position = " + myPlane.Position);
                            }
                        }
                    }
                    else
                    {
                        Vector3 myResponsedPosition;
                        myResponsedPosition.y = Mathf.Max(myOnePlanePars.GAlt * myFeet - myAirport_ALt, 0.0f); // Высота
                        Vector2d worldPosition = Conversions.GeoToWorldPosition(myOnePlanePars.Lat, myOnePlanePars.Long, myCenterMercator, myWorldRelativeScale);

                        MyLog(myKey, "ProcData", "=== myFuncThread(): ключ = " + myKey + " !!!! Lat = " + myOnePlanePars.Lat + " Long = " + myOnePlanePars.Long + " myCenterMercator = " + myCenterMercator + " myWorldRelativeScale = " + myWorldRelativeScale + " worldPosition = " + worldPosition);

                        myResponsedPosition.x = (float)worldPosition.x;
                        myResponsedPosition.z = (float)worldPosition.y;
                        myPlane.Position = myResponsedPosition + myPosShift;
                        MyLog(myKey, "ProcData", "=== myFuncThread(): ключ = " + myKey + " Истории еще нет. Данные полета считаем достоверными, летим по ним. myPlane.Position = " + myPlane.Position);
                    }

                    // Углы
                    myPlane.Euler.y = myOnePlanePars.Trak; // устанавливаем только курсовой угол (угол рыскания)

                    // Скорость
                    myPlane.Speed.y = myOnePlanePars.Vsi * myFeet / 60.0f;
                    myPlane.Speed.x = myOnePlanePars.Spd * myKnot * Mathf.Sin(myOnePlanePars.Trak * Mathf.Deg2Rad);
                    myPlane.Speed.z = myOnePlanePars.Spd * myKnot * Mathf.Cos(myOnePlanePars.Trak * Mathf.Deg2Rad);

                    MyLog(myKey, "ProcData", "=== myFuncThread(): ключ = " + myKey + " BackData = " + myPlaneBackData + " myPlaneFrozen = " + myPlaneFrozen + " myPlaneBadPos = " + myPlaneBadPos);

                    if (myPlaneBackData)
                    {
                        myPlane.PredictionReason = "BackData";
                    }
                    else if (myPlaneFrozen)
                    {
                        myPlane.PredictionReason = "Frozen";
                    }
                    else if (myPlaneBadPos)
                    {
                        myPlane.PredictionReason = "BadPos";
                    }
                    else
                    {
                        myPlane.PredictionReason = "Web";
                    }

                    MyLog(myKey, "ProcData", "=== myFuncThread(): ключ = " + myKey + " Заполнили экземпляр малой структуры.");

                    // Преобразование и разбор данных завершены. Запишем измененные данные в словари и пополним историю

                    if (myPlaneIsNew)
                    {
                        myAllPlanesPars.Add(myKey, myOnePlanePars); // Добавим большую структуру в словарь myAllPlanesPars
                        myPlaneVis.Add(myKey, myPlane); // Добавим малую структуру в словарь myPlaneVis
                        // Заведем для нового самолета историю
                        myOnePlaneHist.Time = new List<long> { myStartProcTime };
                        myOnePlaneHist.PosTime = new List<long> { myOnePlanePars.PosTime };
                        myOnePlaneHist.PredictionReason = new List<String> { myPlane.PredictionReason };
                        myOnePlaneHist.Position = new List<Vector3> { myPlane.Position };
                        myOnePlaneHist.Euler = new List<Vector3> { myPlane.Euler };
                        myOnePlaneHist.Speed = new List<Vector3> { myPlane.Speed };
                        myPlanesHistory.Add(myKey, myOnePlaneHist);
                        MyLog(myKey, "ProcData", "=== myFuncThread(): ключ = " + myKey + " Создали самолет, завели новую историю и добавили по записи во все словари.");
                    }
                    else
                    {
                        myAllPlanesPars[myKey] = myOnePlanePars; // Обновим большую структуру в словаре myAllPlanesPars
                        myPlane.GO = myPlaneVis[myKey].GO; // Указатель на Game Object
                        myPlane.Banner1 = myPlaneVis[myKey].Banner1;
                        myPlane.Banner1Call = myPlaneVis[myKey].Banner1Call;
                        myPlane.Banner1Icao = myPlaneVis[myKey].Banner1Icao;
                        myPlane.Banner1PReason = myPlaneVis[myKey].Banner1PReason;
                        myPlane.Banner1Model = myPlaneVis[myKey].Banner1Model;
                        myPlane.Banner1Alt = myPlaneVis[myKey].Banner1Alt;
                        myPlane.Banner1Panel = myPlaneVis[myKey].Banner1Panel;
                        myPlane.Model = myPlaneVis[myKey].Model;
                        myPlaneVis[myKey] = myPlane; // Обновим малую структуру в словаре myPlaneVis
                        // Пополним историю самолета
                        // Извлечем из словаря структуру с историей
                        myOnePlaneHist = myPlanesHistory[myKey];
                        // Пополним каждый исторический массив
                        myOnePlaneHist.Time.Add(myStartProcTime);
                        myOnePlaneHist.PosTime.Add(myOnePlanePars.PosTime);
                        myOnePlaneHist.PredictionReason.Add(myPlane.PredictionReason);
                        myOnePlaneHist.Position.Add(myPlane.Position);
                        myOnePlaneHist.Euler.Add(myPlane.Euler);
                        myOnePlaneHist.Speed.Add(myPlane.Speed);
                        // Держим в истории конечное количество точек
                        if (myOnePlaneHist.Time.Count > 50)
                        {
                            myOnePlaneHist.Time.RemoveAt(0);
                            myOnePlaneHist.PosTime.RemoveAt(0);
                            myOnePlaneHist.PredictionReason.RemoveAt(0);
                            myOnePlaneHist.Position.RemoveAt(0);
                            myOnePlaneHist.Euler.RemoveAt(0);
                            myOnePlaneHist.Speed.RemoveAt(0);
                        }
                        // Обновим историю в словаре
                        myPlanesHistory[myKey] = myOnePlaneHist;
                        MyLog(myKey, "ProcData", "=== myFuncThread(): ключ = " + myKey + " Обновили по записи во всех словарях. Точек в истории: " + myOnePlaneHist.Time.Count);
                        // Распечатаем историю
                        MyLog(myKey, "Time\tPosTime\tPredict\tPosX\tPosY\tPosZ\tPitch\tYaw\tRoll\tSpeedX\tSpeedY\tSpeedZ");
                        for (int j = 0; j < myOnePlaneHist.Time.Count; j++)
                        {
                            String myLine = myOnePlaneHist.Time[j] + "\t" + myOnePlaneHist.PosTime[j] + "\t" + myOnePlaneHist.PredictionReason[j] + "\t" +
                                myOnePlaneHist.Position[j].x + "\t" + myOnePlaneHist.Position[j].y + "\t" + myOnePlaneHist.Position[j].z + "\t" +
                                myOnePlaneHist.Euler[j].x + "\t" + myOnePlaneHist.Euler[j].y + "\t" + myOnePlaneHist.Euler[j].z + "\t" +
                                myOnePlaneHist.Speed[j].x + "\t" + myOnePlaneHist.Speed[j].y + "\t" + myOnePlaneHist.Speed[j].z;
                            MyLog(myKey, myLine);
                        }
                    }
                }
            }
            else // Если новых полетных данных от сервера еще не получили, то строим прогнозы для каждого самолета и летим по ним
            {
                long myCurTime = myStopWatch.ElapsedMilliseconds - myStartTime;
                myLastDeltaTime = myCurTime - myStartProcTime; // Время последнего выполненного полного цикла обработки данных
                myStartProcTime = myCurTime; // Время начала нового цикла
                MyLog("RawData", "ProcData", "=== myFuncThread(): Новые данные от myFuncGetData() еще не поступили myLastDeltaTime = " + myLastDeltaTime + " myStartProcTime = " + myStartProcTime);

                List<String> myKeys = new List<String>(myPlaneVisKeys);
                foreach (String myKey in myKeys)
                {
                    // Большая и Малая структуры данных и история данного самолета
                    myPlaneParameters myOnePlanePars = myAllPlanesPars[myKey];
                    MyPlaneVisual myPlane = myPlaneVis[myKey];
                    MyFlightHistory myOnePlaneHist = myPlanesHistory[myKey];
                    MyLog("ProcData", "=== myFuncThread(): Большая и Малая структуры данных и история данного самолета");
                    // Готовим прогноз
                    int k = myOnePlaneHist.Time.Count; // число записей в истории
                    MyLog("ProcData", "=== myFuncThread(): k = myOnePlaneHist.Time.Count, k=" + k);
                    Vector3 myLastPosition = myOnePlaneHist.Position[k - 1];
                    Vector3 myPredictedPosition = myLastPosition + myOnePlaneHist.Speed[k - 1] * myLastDeltaTime / 1000;
                    MyLog(myKey, "=== myFuncThread(): Новые данные от myFuncGetData() еще не поступили");
                    MyLog(myKey, "ProcData", "=== myFuncThread(): ключ = " + myKey + " Прогноз. LastDeltaTime = " + myLastDeltaTime + " Previous Position =" + myOnePlaneHist.Position[k - 1] + " Previous Speed = " + myOnePlaneHist.Speed[k - 1] + " Predicted Position = " + myPredictedPosition);
                    // и "летим" по нему
                    myPlane.Position = myPredictedPosition;
                    MyLog(myKey, "=== myFuncThread(): ключ = " + myKey + " Летим по прогнозу. myPlane.Position = " + myPlane.Position);
                    myPlane.PredictionReason = "NoData";
                    // Обновим малую структуру в словаре myPlaneVis
                    myPlaneVis[myKey] = myPlane;

                    // Пополним историю самолета
                    // Пополним каждый исторический массив
                    myOnePlaneHist.Time.Add(myStartProcTime);
                    myOnePlaneHist.PosTime.Add(myOnePlanePars.PosTime);
                    myOnePlaneHist.PredictionReason.Add(myPlane.PredictionReason);
                    myOnePlaneHist.Position.Add(myPlane.Position);
                    myOnePlaneHist.Euler.Add(myPlane.Euler);
                    myOnePlaneHist.Speed.Add(myPlane.Speed);
                    // Держим в истории конечное количество точек
                    if (myOnePlaneHist.Time.Count > 50)
                    {
                        myOnePlaneHist.Time.RemoveAt(0);
                        myOnePlaneHist.PosTime.RemoveAt(0);
                        myOnePlaneHist.PredictionReason.RemoveAt(0);
                        myOnePlaneHist.Position.RemoveAt(0);
                        myOnePlaneHist.Euler.RemoveAt(0);
                        myOnePlaneHist.Speed.RemoveAt(0);
                    }
                    // Обновим историю в словаре
                    myPlanesHistory[myKey] = myOnePlaneHist;
                    MyLog(myKey, "=== myFuncThread(): ключ = " + myKey + " Обновили по записи в малом и историческом словарях. Точек в истории: " + myOnePlaneHist.Time.Count);
                    // Распечатаем историю
                    MyLog(myKey, "Time\tPosTime\tPredict\tPosX\tPosY\tPosZ\tPitch\tYaw\tRoll\tSpeedX\tSpeedY\tSpeedZ");
                    for (int j = 0; j < myOnePlaneHist.Time.Count; j++)
                    {
                        String myLine = myOnePlaneHist.Time[j] + "\t" + myOnePlaneHist.PosTime[j] + "\t" + myOnePlaneHist.PredictionReason[j] + "\t" +
                            myOnePlaneHist.Position[j].x + "\t" + myOnePlaneHist.Position[j].y + "\t" + myOnePlaneHist.Position[j].z + "\t" +
                            myOnePlaneHist.Euler[j].x + "\t" + myOnePlaneHist.Euler[j].y + "\t" + myOnePlaneHist.Euler[j].z + "\t" +
                            myOnePlaneHist.Speed[j].x + "\t" + myOnePlaneHist.Speed[j].y + "\t" + myOnePlaneHist.Speed[j].z;
                        MyLog(myKey, myLine);
                    }
                }
            }

            MyLog("RawData", "ProcData", "=== myFuncThread(): Первичная обработка данных завершена.");
            myPrimaryDataProc = false;
            mySecondaryDataProc = true;
        }
    }


    // Вторичная обработка полетных данные в корутине
    IEnumerator myFuncProcData()
    {

        while (true)
        {
            //MyLog("**********************************************************************");

            // Ждем заверешения первичной обработки данных  в потоке
            MyLog("RawData", "ProcData", "--------------------------------------------------------");
            MyLog("RawData", "ProcData", "%%% myFuncProcData(): Начинаем ждать первичную обработку данных");
            long myStartWaitTime = myStopWatch.ElapsedMilliseconds - myStartTime;
            while (myPrimaryDataProc)
            {
                yield return new WaitForSeconds(0.01f);
            }

            long mySecondProcStartTime = myStopWatch.ElapsedMilliseconds - myStartTime;

            MyLog("RawData", "ProcData", "%%% myFuncProcData(): Обнаружено, что первичная обработка данных завершена. myPrimaryDataProc = " + myPrimaryDataProc + ". Текущее время = " + (myStopWatch.ElapsedMilliseconds - myStartTime) + ". Время ожидания первичной обработки = " + (myStopWatch.ElapsedMilliseconds - myStartTime - myStartWaitTime));
            MyLog("RawData", "ProcData", "%%% myFuncProcData(): Начало вторичной обработки данных. Выполняется в основном потоке в корутине myFuncProcData()");

            // Обработаем все самолеты по словарю myPlaneVis - создадим новые, удалим устаревшие

            // Список ключей малого словаря (сформирован из коллекции ключей)
            List<String> myKeys = new List<String>(myPlaneVis.Keys);
            for (int i = 0; i < myKeys.Count; i++)
            {
                MyLog("ProcData", "%%% myFuncProcData(): Самолет № " + i + ". Ключ = " + myKeys[i]);
                // Признак того, что самолет подлежит удалению
                bool myPlaneToDelete = false;
                // Признак того, что самолет приземлился
                bool myPlaneLanded = false;

                // Извлечем структуры из словарей
                MyPlaneVisual myPlane = myPlaneVis[myKeys[i]];
                //myPlaneParameters myOnePlanePars = myAllPlanesPars[myKeys[i]];

                // Добавим новый самолет и уточним его запись в словаре myPlaneVis (добавим указатели на созданные объекты)
                if (!myPlane.GO) // в структуре еще нет указателя на самолет
                {
                    MyLog("ProcData", "%%% myFuncProcData(): Создадим новый самолет");
                    // Создадим новый самолет
                    Transform myNewPlane = Instantiate(mySamplePlane);
                    myNewPlane.name = myKeys[i];
                    myNewPlane.gameObject.SetActive(true);
                    myNewPlane.parent = myPlanesController;
                    // Указатель на вновь созданный Game Object
                    myPlane.GO = myNewPlane.gameObject;
                    Transform myObjTr1 = myPlane.GO.transform.GetChild(0); // дочерний объект пока один
                    // Это канвас баннера с краткой информацией
                    myPlane.Banner1 = myObjTr1;
                    for (int k = 0; k < myObjTr1.childCount; k++)
                    {
                        Transform myObjTr2 = myObjTr1.GetChild(k);
                        switch (myObjTr2.name)
                        {
                            case "Call": // Позывной
                                myPlane.Banner1Call = myObjTr2.GetComponent<Text>();
                                myPlane.Banner1Call.text = myPlane.Call;
                                break;
                            case "Icao": // Код ИКАО
                                myPlane.Banner1Icao = myObjTr2.GetComponent<Text>();
                                myPlane.Banner1Icao.text = myPlane.Icao;
                                myPlane.Banner1Icao.gameObject.SetActive(myBanner1AddInfo); // Включить/выключить текстовое поле
                                break;
                            case "PReason": // Причина движения по прогнозу
                                myPlane.Banner1PReason = myObjTr2.GetComponent<Text>();
                                myPlane.Banner1PReason.text = myPlane.PredictionReason;
                                myPlane.Banner1PReason.gameObject.SetActive(myBanner1AddInfo); // Включить/выключить текстовое поле
                                break;
                            case "Model":
                                myPlane.Banner1Model = myObjTr2.GetComponent<Text>(); // Текст второй строки баннера
                                String myText = MyFuncModelName(myAllPlanesPars[myKeys[i]].Mdl);
                                if(myText.Length <= 12)
                                {
                                    myPlane.Banner1Model.fontSize = 75;
                                }
                                myPlane.Banner1Model.text = myText;// Модель
                                break;
                            case "Alt":
                                myPlane.Banner1Alt = myObjTr2.GetComponent<Text>(); // Текст третьей строки баннера
                                if (mySI)
                                {
                                    myPlane.Banner1Alt.text = "Alt(m)=" + myPlane.Position.y; // Высота в метрах
                                }
                                else
                                {
                                    myPlane.Banner1Alt.text = "Alt(ft)=" + myPlane.Position.y * myFeet; // Высота в футах
                                }
                                break;
                            case "Panel":
                                myPlane.Banner1Panel = myObjTr2.GetComponent<Image>(); // Фоновая картинка баннера
                                break;
                        }
                    }
                    // Сформируем имя 3D модели по ICAO кодам самолета и авиакомпании
                    String myModelName; // первая часть имени - модель самолета
                    if(myAllPlanesPars[myKeys[i]].Type == null)
                    {
                        myModelName = "A320"; // Если код модели самолета пустой, то пусть будет A320
                    }
                    else // Если код модели самолета не пустой, то берем из имя словаря myKnownPlanes
                    {
                        if (myKnownPlanes.ContainsKey(myAllPlanesPars[myKeys[i]].Type))
                        {
                            myModelName = myKnownPlanes[myAllPlanesPars[myKeys[i]].Type];
                        }
                        else
                        {
                            myModelName = "A320"; // Если не знаем такой модели самолета, то тоже пусть будет A320
                        }
                    }

                    String myAirlineName; // вторая часть имени - название (сокращенное) авиакомпании, берем из словаря myKnownPlanes
                    if (myAllPlanesPars[myKeys[i]].OpIcao == null)
                    {
                        myAirlineName = ""; // Если код авиакомпании пустой, то пусть название будет ""
                    }
                    else // Если код авиакомпании не пустой, берем из ее обозначение из словаря myKnownAirlines
                    {
                        if (myKnownAirlines.ContainsKey(myAllPlanesPars[myKeys[i]].OpIcao))
                        {
                            myAirlineName = myKnownAirlines[myAllPlanesPars[myKeys[i]].OpIcao];
                        }
                        else
                        {
                            myAirlineName = ""; // Если не знаем такую автакомпанию, то обозначение тоже пусть будет ""
                        }
                    }

                    String my3DName; // Сформируем имя для 3D модели
                    if (myAirlineName != "") // если знаем такую авиакомпанию
                    {
                        String my3DNam = myModelName + "_" + myAirlineName; // предварительно имя 3D модели состоит из названия модели самолета и названия авиакомпании
                        // Проверим, есть ли 3D модель с таким именем
                        if (myPlanes3D.ContainsKey(my3DNam))
                        {
                            my3DName = my3DNam; // если есть - запоминаем в переменной my3DName
                        }
                        else
                        {
                            my3DName = myModelName; // если нет - имя 3D модели будет без только из названия самолета, без названия авиакомпании.
                        }
                    }
                    else // если не знаем авиакомпанию, имя 3D модели будет без только из названия самолета
                    {
                        my3DName = myModelName;
                    }

                    //Отладка
                    print("======= Код модели самолета: " + myAllPlanesPars[myKeys[i]].Type + ", модель самолета: " + myModelName + ", код компании: " + myAllPlanesPars[myKeys[i]].OpIcao + ", название компании: " + myAirlineName + ", имя 3D модели: " + my3DName);
                    //Еще раз проверим имя 3D модели
                    if (myPlanes3D.ContainsKey(my3DName))
                    {
                        print("======= Есть такая 3D модель!");
                    }
                    else
                    {
                        print("======= ОШИБКА! Нет такой 3D модели: " + my3DName);
                    }

                    // Создадим копию 3D модели
                    Transform myPlane3D = Instantiate(myPlanes3D[my3DName]);

                    // Переместим созданную 3D модель в дочерние объекты самолета
                    myPlane3D.parent = myPlane.GO.transform;
                    myPlane3D.localPosition = Vector3.zero;
                    // Запишем 3D модель (трансформ) в малую структуру
                    myPlane.Model = myPlane3D;

                    MyLog("ProcData", "%%% myFuncProcData(): Установим положение самолета");
                    myPlane.GO.transform.position = myPlane.Position;
                    myPlane.GO.transform.eulerAngles = myPlane.Euler;
                    myPlaneVis[myKeys[i]] = myPlane; // Уточним малую структуру в словаре
                }
                // Удалим устаревший самолет и его записи во всех словарях. Также удалим самолет, если он "приземлися"
                else if (((myResponseTime - myPlane.Time) > 15000))
                {
                    myPlaneToDelete = true;
                    MyLog("ProcData", myKeys[i], "%%% myFuncProcData(): Будем удалять устаревший самолет " + myKeys[i] + ". Время последнего приема данных от сервера = " + myResponseTime + ", время поступления последних данных о самолете = " + myPlane.Time);
                }
                else
                {
                    // Проверим высоту по истории в последних точках. Если везде 0, то значит самолет приземлился
                    MyFlightHistory myOnePlaneHist = myPlanesHistory[myKeys[i]]; // история данного самолета
                    int myNum = myOnePlaneHist.Time.Count;
                    MyLog("ProcData", "%%% myFuncProcData(): Будем проверять, не сел ли самолет. Записей в истории всего " + myNum);
                    if (myNum < 3) // Недостаточно длинная история
                    {
                        MyLog("ProcData", "%%% myFuncProcData(): Записей недостаточно. По этому критерию - не сел");
                        myPlaneLanded = false;
                    }
                    else
                    {
                        myPlaneLanded = true;
                        for (int j = Mathf.Min(myNum, 12); j > 0; j--) // Здесь устанавливаем количество проверяемых точек
                        {
                            MyLog("ProcData", "%%% myFuncProcData(): Проверяем. Запись номер " + (myNum - j) + ": Высота = " + myOnePlaneHist.Position[myNum - j].y);
                            // Проверяем. Если высота хоть в одной точке больше 0 - значит не приземлился
                            if (myOnePlaneHist.Position[myNum - j].y > 0.0f)
                            {
                                myPlaneLanded = false;
                                break;
                            }
                        }
                        MyLog("ProcData", "%%% myFuncProcData(): Итого: myPlaneLanded = " + myPlaneLanded);
                    }
                    if (myPlaneLanded)
                    {
                        // Добавим запись о самолете в словарь приземлившихся
                        myLandedPlanes.Add(myKeys[i], myStartProcTime);
                        myPlaneToDelete = true;
                        MyLog("ProcData", myKeys[i], "%%% myFuncProcData(): Самолет " + myKeys[i] + " сел. Будем удалять ");
                    }
                }

                if (myPlaneToDelete) // Удаляем самолет и его записи во всех словарях
                {
                    MyLog("ProcData", "%%% myFuncProcData(): Удалим устаревший самолет. Время последнего начала последнего цикла обработки = " + myStartProcTime + ", время последних данных о самолете = " + myPlane.Time);
                    myAllPlanesPars.Remove(myKeys[i]); // Словарь - Большая структура
                    myPlaneVis.Remove(myKeys[i]); // Словарь - Малая структура
                    myPlanesHistory.Remove(myKeys[i]); // Словарь - История полета
                    if (mySlowPlanes.ContainsKey(myKeys[i])) // Словарь - "Медленные" самолеты
                    {
                        mySlowPlanes.Remove(myKeys[i]); // Удалим самолет из словаря медленных
                        mySlowPlanesCount = mySlowPlanes.Count; // Поправим счетчик медленных самолетов
                    }
                    // Проверим, не сидит ли у него случайно в дочерних объектах ступа с камерой.
                    for (int j = 0; j < myPlane.GO.transform.childCount; j++)
                    {
                        Transform myObjTr = myPlane.GO.transform.GetChild(j);
                        if (myObjTr.name == "Mortar") // Если сидит, то выведем ее в корень сцены
                        {
                            myObjTr.parent = null;
                        }
                    }
                    Destroy(myPlane.GO);

                    // Закроем лог-файлы и удалим записи из словаря лог-файлов
                    myRecFile[myKeys[i]].Close();
                    myRecFile[myKeys[i] + "_Data"].Close();
                    myRecFile.Remove(myKeys[i]);
                    myRecFile.Remove(myKeys[i] + "_Data");

                }
                else // Самолет не удаляем - определим ему угол тангажа
                {
                    MyFlightHistory myOnePlaneHist = myPlanesHistory[myKeys[i]]; // история данного самолета
                    int myNum = myOnePlaneHist.Time.Count;
                    // В истории должно быть не меньше двух точек
                    if (myNum >= 2)
                    {
                        // Запомним положение и углы
                        Vector3 myPos = myPlane.GO.transform.position;
                        Vector3 mEu = myPlane.GO.transform.eulerAngles;
                        // Временно поставим самолет в предпоследнее положение
                        myPlane.GO.transform.position = myOnePlaneHist.Position[myNum - 2];
                        // Временно повернем самолет в сторону последнего положения
                        Vector3 myLastPos = myOnePlaneHist.Position[myNum - 1];
                        myPlane.GO.transform.LookAt(myLastPos);
                        // Вот он - угол тангажа
                        float myPitch = myPlane.GO.transform.eulerAngles.x;
                        if(myLastPos.y <= 0.0f)
                        {
                            myPitch = 0.0f;
                        }
                        // Вернем самолет на место
                        myPlane.GO.transform.position = myPos;
                        myPlane.GO.transform.eulerAngles = mEu;
                        // Пропишем угол тангажа в параметрах самолета и в его истории
                        myPlane.Euler.x = myPitch;
                        myPlaneVis[myKeys[i]] = myPlane; // Уточним малую структуру в словаре
                        myOnePlaneHist.Euler[myNum - 1] = new Vector3(myPitch, myOnePlaneHist.Euler[myNum - 1].y, myOnePlaneHist.Euler[myNum - 1].z);
                        myPlanesHistory[myKeys[i]] = myOnePlaneHist;
                    }

                    // Есть ли у самолета скорость, достаточная для полета? (Пусть скорость будет выше 72 км/час = 20 метров/сек)
                    // Если меньше или равна - прячем самолет, если больше - показываем
                    // Также проверяем и корректируем словарь-список медленных самолетов

                    bool myLowSpeedDetected = myPlane.Speed.sqrMagnitude <= myLowSpeedSqr; // Сравниваем квадрат скорости
                    bool myPlaneAlreadyHasLowSpeed = mySlowPlanes.ContainsKey(myKeys[i]);

                    if(myLowSpeedDetected && myPlaneAlreadyHasLowSpeed) // Скорость низкая, как и была раньше
                    {
                        MyLog(myKeys[i], "ProcData", "%%% myFuncProcData(): ключ = " + myKeys[i] + ". Скорость низкая, как и была до того: самолет остается спрятанным");
                    }
                    else if(myLowSpeedDetected && !myPlaneAlreadyHasLowSpeed) // Обнаружена низкая скорость (ранее была или неизвестная, или высокая)
                    {
                        MyLog(myKeys[i], "ProcData", "%%% myFuncProcData(): ключ = " + myKeys[i] + ". Обнаружена низкая скорость: прячем самолет");
                        mySlowPlanes.Add(myKeys[i], false); // Добавим самолет в словарь медленных
                        mySlowPlanesCount = mySlowPlanes.Count; // Поправим счетчик медленных самолетов
                        myPlane.GO.SetActive(false); // Прячем самолет
                    }
                    else if (!myLowSpeedDetected && myPlaneAlreadyHasLowSpeed) // Обнаружена высокая скорость (ранее была низкая)
                    {
                        MyLog(myKeys[i], "ProcData", "%%% myFuncProcData(): ключ = " + myKeys[i] + ". Обнаружена высокая скорость после низкой: показываем самолет");
                        mySlowPlanes.Remove(myKeys[i]); // Удалим самолет из словаря медленных
                        mySlowPlanesCount = mySlowPlanes.Count; // Поправим счетчик медленных самолетов
                        myPlane.GO.SetActive(true); // Показываем самолет
                    }
                    else  // Скорость высокая, как и была раньше
                    {
                        MyLog(myKeys[i], "ProcData", "%%% myFuncProcData(): ключ = " + myKeys[i] + ". Скорость высокая, как и была до того: самолет продолжает отображаться");
                    }

                }

                myPlane.Banner1PReason.text = myPlane.PredictionReason; // Отобразим на баннере самолета причину полета по прогнозу

                yield return new WaitForEndOfFrame();
            }

            // Отчитаемся о результатах

            mySceenMessage.text = "ВС в зоне: " + (myKeys.Count - mySlowPlanesCount);

            long myCurTime = myStopWatch.ElapsedMilliseconds - myStartTime;
            long myWorkTime = myCurTime - myStartProcTime;
            MyLog("ProcData", "%%% myFuncProcData(): Всего самолетов в словарях: Большая структура = " + myAllPlanesPars.Count + ", малая структура =  " + myKeys.Count + ", из них скрытых (медленных) = " + mySlowPlanesCount);
            MyLog("RawData", "ProcData", "**********************************************************************");
            MyLog("RawData", "ProcData", "%%% myFuncProcData(): Завершение вторичной обработки. Время вторичной обработки = " + (myCurTime - mySecondProcStartTime) + " Время всей обработки = " + myWorkTime);


            // Переждать до конца рекомендованного времени цикла, секунд (если обработка заняла времени меньше)
            float myWaitTime = Mathf.Max(0.0f, (myCycleTime - myWorkTime / 1000.0f));
            yield return new WaitForSeconds(myWaitTime);
            MyLog("RawData", "ProcData", "%%% myFuncProcData(): Переждали еще секунд: " + myWaitTime + " Разрешаю начать новый цикл обработки");

            mySecondaryDataProc = false;
            myPrimaryDataProc = true;

        }
    }



    // Продолжим заполнять большую структуру из объекта JSON
    // i - порядковый номер в JSON массиве acList[] (каждая запись соответствует данным одного самолета)
    // myKey - ключ текущего самолета
    private myPlaneParameters MyFuncBigStructure(myPlaneParameters myOnePlanePars, dynamic myJObj, int i, String myKey)
    {
        Type myPPType = typeof(myPlaneParameters); // тип объекта "myPlaneParameters"
        JToken myJPlanePars = myJObj.acList[i]; // данные одного самолета в исходном JSON

        // Строка для лога
        StringBuilder myLine = new StringBuilder(i + " key=" + myKey + " Icao=" + myOnePlanePars.Icao + " Id=" + myOnePlanePars.Id + " Lat=" + myOnePlanePars.Lat + " Long=" + myOnePlanePars.Long);
        StringBuilder myTabLine = new StringBuilder(myKey + "\t" + myOnePlanePars.Icao + "\t" + myOnePlanePars.Id + "\t" + myOnePlanePars.Lat + "\t" + myOnePlanePars.Long + "\t" + myOnePlanePars.CMsgs);

        // Разберем данные по полям и заполним большую структуру
        foreach (JToken myChild in myJPlanePars.Children())
        {
            String myName = myChild.Path.Split(new Char[] { '.' })[1]; // имя поля в исходном JSON
            // Не выполнять для уже заполненных полей
            if ("Id,Icao,Lat,Long,CMsgs".Contains(myName))
            {
                continue;
            }
            Type myType = myPlaneParsType[myName]; // тип поля - находим по имени поля в словаре myPlaneParsType (описание полей большой структуры)
            String myValue = myChild.First.ToString(); // значение поля в исходном JSON
            System.Reflection.FieldInfo myFieldInfo = myPPType.GetField(myName); // метаинформация поля с именем myName из большой структуры параметров самолета

            // Преобразуем значения поля в исходном JSON в соответствии с его типом в большой структуре и запишем в соответствующее поле в большой структуре
            if (myType == typeof(int))
            {
                int myVal;
                if (Int32.TryParse(myValue, out myVal)) // преобразование типа к int
                {
                    myFieldInfo.SetValue(myOnePlanePars, myVal); // запись в поле экземпляра myOnePlanePars большой структуры
                }
                else
                {
                    MyLog("Ошибка преобразования данных перед записью в параметры большой структуры");
                }
            }
            else if (myType == typeof(bool)) // преобразование типа к bool 
            {
                bool myVal;
                if (Boolean.TryParse(myValue, out myVal))
                {
                    myFieldInfo.SetValue(myOnePlanePars, myVal); // запись в поле экземпляра myOnePlanePars большой структуры
                }
                else
                {
                    MyLog("Ошибка преобразования данных перед записью в параметры большой структуры");
                }
            }
            else if (myType == typeof(String)) // преобразование типа String не требуется
            {
                myFieldInfo.SetValue(myOnePlanePars, myValue); // запись в поле экземпляра myOnePlanePars большой структуры
            }
            else if (myType == typeof(float)) // преобразование типа к float
            {
                float myVal;
                if (Single.TryParse(myValue, out myVal))
                {
                    myFieldInfo.SetValue(myOnePlanePars, myVal); // запись в поле экземпляра myOnePlanePars большой структуры
                }
                else
                {
                    MyLog("Ошибка преобразования данных перед записью в параметры большой структуры");
                }
            }
            else if (myType == typeof(long)) // преобразование типа к long
            {
                long myVal;
                if (Int64.TryParse(myValue, out myVal))
                {
                    myFieldInfo.SetValue(myOnePlanePars, myVal); // запись в поле экземпляра myOnePlanePars большой структуры
                }
                else
                {
                    MyLog("Ошибка преобразования данных перед записью в параметры большой структуры");
                }
            }
            else if (myType == typeof(DateTime)) // преобразование типа к DateTime
            {
                DateTime myVal;
                if (DateTime.TryParse(myValue, out myVal))
                {
                    myFieldInfo.SetValue(myOnePlanePars, myVal); // запись в поле экземпляра myOnePlanePars большой структуры
                }
                else
                {
                    MyLog("Ошибка преобразования данных перед записью в параметры большой структуры");
                }
            }
            else
            {
                MyLog("Ошибка! Тип " + myType + " не описан в коде программы");
            }
            // Добавить имя и значение к концу строки для лога
            myLine.Append(" " + myName + "=" + myFieldInfo.GetValue(myOnePlanePars));
            myTabLine.Append("\t" + myFieldInfo.GetValue(myOnePlanePars));
        }
        // Записать строки в логи
        MyLog("ProcData", "=== myFuncThread()/MyFuncBigStructure(): " + myLine + "\t");
        MyLog(myKey + "_Data", myTabLine + "\t" + (myStopWatch.ElapsedMilliseconds - myStartTime), false);
        return myOnePlanePars;
    }


    private void OnApplicationQuit()
    {
        // Закрыть все открытые лог-файлы
        List<String> myKeys = new List<String>(myRecFile.Keys);
        for (int i = 0; i < myKeys.Count; i++)
        {
            myRecFile[myKeys[i]].Close();
        }
        // Закрыть фоновый поток
        myFightDataThread.Abort();
    }


    long myFrameCount = 0;
    int myLogFrameCount = 0;

    // Update is called once per frame
    void Update()
    {
        bool myAddInfoWasChaged = false; // Признак изменения дополнительной информации для отображения на баннере самолета с краткой информацией

        // Сначала чуть-чуть управления
        if (Input.GetKeyDown("-")) // Клавиша "минус": "Опустить" все самолеты на 10 метров (увеличить высоту аэропорта над уровнем моря)
        {
            myAirport_ALt += 10.0f;
            myWorldMessage.myFuncShowMessage("Самолеты ниже на 10 м.\nНовая высота Н.У.М. = " + myAirport_ALt, 3.0f);
        }
        else if (Input.GetKeyDown("=")) // Клавиша "плюс": "Поднять" все самолеты на 10 метров (уменьшить высоту аэропорта над уровнем моря)
        {
            myAirport_ALt -= 10.0f;
            myWorldMessage.myFuncShowMessage("Самолеты выше на 10 м.\nНовая высота Н.У.М. = " + myAirport_ALt, 3.0f);
        }
        else if (Input.GetKeyDown("i")) // Клавиша "i": Вывести / убрать с баннера самолета с краткой информацией дополнительную информацию
        {
            myBanner1AddInfo = !myBanner1AddInfo;
            myAddInfoWasChaged = true; // Пока только установим признак изменения, само изменение позже, в цикле обработки всех самолетов
        }
        else if (Input.GetKeyDown("c")) // Клавиша "с": Переключить метрическую систему СИ/футы-мили
        {
            mySI = !mySI;
        }

        // Основная часть - управление положением самолетов

        if (myPlaneVisValues != null)
        {
            // Текущее время сеанса
            long myCurTime = myStopWatch.ElapsedMilliseconds - myStartTime;
            // Время, оставшееся до момента, в котором мы собираемся привести самолет в очередную точку траектории
            long myLeftToTargetTime = myStartProcTime + myLag - myCurTime;

            // Отладка
            myFrameCount++; //
            bool myWriteLog = false; //
            float myDeltaY = 0.0f; //

            if(myLogFrameCount++ > 8)
            {
                myWriteLog = true;
                MyLog("Update", "===================== Frame=" + myFrameCount + " LogFrame=" + myLogFrameCount + " Time=" + myCurTime + " LeftToTargetTime=" + myLeftToTargetTime);
                myLogFrameCount = 0;
            }

            // Создаем список ключей из коллекции, чтобы не было ошибки InvalidOperationException: Collection was modified; enumeration operation may not execute (может быть изменена в фоновом потоке)
            List<String> myKeys = new List<String>(myPlaneVisKeys);

            // Двигаем самолеты
            foreach (String myKey in myKeys)
            {
                MyPlaneVisual myPlane = myPlaneVis[myKey];
                if (myPlane.GO)
                {

                    // Еше чуть-чуть управления
                    if (myAddInfoWasChaged) // Клавиша "i" была нажата: Вывести / убрать с баннера самолета с краткой информацией дополнительную информацию
                    {
                        // Включить/выключить текстовые поля
                        myPlane.Banner1Icao.gameObject.SetActive(myBanner1AddInfo);
                        myPlane.Banner1PReason.gameObject.SetActive(myBanner1AddInfo);
                    }

                    // Перемещение самолета
                    //myPlane.GO.transform.position = myPlane.GO.transform.position + myPlane.Speed * Time.deltaTime;

                    Vector3 myPos = myPlane.GO.transform.position;
                    Vector3 myEu = myPlane.GO.transform.eulerAngles;
                    if (myLeftToTargetTime > 0)
                    {
                        myPos = myPos + (myPlane.Position - myPos) * Time.deltaTime * 1000 / myLeftToTargetTime;
                        if (myPos.y < 0)
                        {
                            myPos.y = 0;
                        }

                        // Поставим самолет в новую точку на траектории
                        myPlane.GO.transform.position = myPos;

                        Vector3 myDeltaEu = (myPlane.Euler - myEu); // угол, на который нужно будет повернуть к концу периода

                        // Отладка
                        if (myWriteLog)
                        {
                            myDeltaY = myDeltaEu.y;
                        } //

                        // Приведем курсовой угол и угол тангажа от (0/360) к (-180/+180)
                        if (myDeltaEu.y > 180.0f)
                        {
                            myDeltaEu.y -= 360.0f;
                        }
                        else if (myDeltaEu.y < -180.0f)
                        {
                            myDeltaEu.y += 360.0f;
                        }
                        if (myDeltaEu.x > 180.0f)
                        {
                            myDeltaEu.x -= 360.0f;
                        }
                        else if (myDeltaEu.x < -180.0f)
                        {
                            myDeltaEu.x += 360.0f;
                        }

                        myPlane.GO.transform.eulerAngles = myEu + myDeltaEu * Time.deltaTime * 1000 / myLeftToTargetTime;

                        // Отладка
                        if (myWriteLog)
                        {
                            MyLog("Update", "Plane=" + myKey + " OldEu=" + myEu.y + " TargetEu=" + myPlane.Euler.y + " DeltaEu=" + myDeltaY + " DeltaEuNorm=" + myDeltaEu.y + " NewEu=" + myPlane.GO.transform.eulerAngles.y);
                        } //
                    }

                    // Масштабирование модели самолета при приближении к земле
                    float myHeight = myPlane.GO.transform.position.y;
                    float myScale = 1.0f;
                    if ((myHeight < 1000.0f) || (myPlane.Model.localScale.x < 30.0f)) // высота, ниже которой начинается масшатбирование - 500 метров
                    {
                        myScale = Mathf.Clamp(((myHeight + 140.0f) / 38.0f), 5.0f, 30.0f);
                        myPlane.Model.localScale = myPlane.GO.transform.localScale * myScale;
                    }

                    // Ориентация баннера
                    myPlane.Banner1.LookAt(Camera.main.transform);
                    // Масштабирование баннера
                    //myDistance = Vector3.Distance(Camera.main.transform.position, myPlane.GO.transform.position);
                    float myDistance = (myPlane.GO.transform.position - Camera.main.transform.position).magnitude;
                    myScale = Mathf.Clamp(myDistance / 5000, 1.0f, 10.0f);
                    myPlane.Banner1.localScale = myPlane.GO.transform.localScale * myScale;
                    // Коррекция положения баннера относительно самолета
                    myPos = myPlane.Banner1.localPosition;
                    myPos.y = 180.0f * myScale + 180.0f;
                    myPlane.Banner1.localPosition = myPos;
                    // Текс баннера (третья строка - высота)
                    if (mySI)
                    {
                        myPlane.Banner1Alt.text = "Alt(m)=" + Math.Round(myPlane.GO.transform.position.y, 2).ToString("####0.00"); // Высота в метрах
                    }
                    else
                    {
                        myPlane.Banner1Alt.text = "Alt(ft)=" + (Math.Round(myPlane.GO.transform.position.y, 2) / myFeet).ToString("####0.00"); // Высота в футах
                    }
                    //myPlane.Banner1Text3.text = "Alt=" + Math.Round(myPlane.GO.transform.position.y, 2) + " Pitch=" + myPlane.GO.transform.eulerAngles.x;
                }
            }

            // Второй баннер с подробной информацией
            if (myBanner2.gameObject.activeInHierarchy && myPlaneVis.ContainsKey(mySelectedPlane))
            {
                myPlaneParameters mySelectedPlaneBigPars = myAllPlanesPars[mySelectedPlane];
                MyPlaneVisual mySelectedPlaneSmallPars = myPlaneVis[mySelectedPlane];

                if (mySI) // метрическая система СИ
                {
                    myBanner2Fields["Alt"].text = "Alt(m)=" + Math.Round(mySelectedPlaneSmallPars.GO.transform.position.y, 2).ToString("####0.00"); // Высота в метрах
                    myBanner2Fields["Speed"].text = "Speed(km/h)=" + Math.Round(mySelectedPlaneBigPars.Spd*1.852f, 2).ToString("####0.00"); // Скорость в км/час
                    myBanner2Fields["VSpd"].text = "VSpeed(m/sec)=" + Math.Round(mySelectedPlaneSmallPars.Speed.y, 2).ToString("####0.00"); // Вертикальная скорость в метр/сек
                    //гг
                }
                else // метрическая система футы/мили
                {
                    myBanner2Fields["Alt"].text = "Alt(ft)=" + (Math.Round(mySelectedPlaneSmallPars.GO.transform.position.y, 2) / myFeet).ToString("####0.00"); // Высота в футах
                    myBanner2Fields["Speed"].text = "Speed(kn)=" + Math.Round(mySelectedPlaneBigPars.Spd, 2).ToString("####0.0"); // Скорость в узлах
                    myBanner2Fields["VSpd"].text = "VSpeed(ft/min)=" + mySelectedPlaneBigPars.Vsi; // Вертикальная скорость в фут/мин
                }
                myBanner2Fields["Trak"].text = "Track=" + Math.Round(mySelectedPlaneBigPars.Trak, 2).ToString("####0.0") + "°"; // Курс самолета в градусах
            }
        }
    }

    // Разобрать полное название модели и вернуть краткое
    String MyFuncModelName(String myLongName)
    {
        if (myLongName == null) // Модель не передана сервером
        {
            return "";
        }
        String myShortName;
        int myWordsNumber = 0;
        String[] myWords = myLongName.Split(new Char[] { ' ' }); // Извлечь список слов, разделенных пробелом

        switch (myWords[0])
        {
            case "Airbus":
            case "Boeing":
            case "Embraer":
            case "Ilyushin":
            case "Antonov":
                myWordsNumber = 2;
                break;
            case "Sukhoi":
            case "Bombardier":
                myWordsNumber = 3;
                break;
        }

        myWordsNumber = Math.Min(myWordsNumber, myWords.Length);

        if (myWordsNumber == 2)
        {
            myShortName = myWords[0] + " " + myWords[1];
        }
        else if (myWordsNumber == 3)
        {
            myShortName = myWords[0] + " " + myWords[1] + " " + myWords[2];
        }
        else // Название модели неизвестно, транслируем полностью
        {
            myShortName = myLongName;
        }
        return myShortName;
    }

    public void myFuncShowBanner2(String myKey)
    {
        // Выбранный самолет
        mySelectedPlane = myKey;
        // Большая структура данных выбранного самолета
        myPlaneParameters myOnePlanePars = myAllPlanesPars[mySelectedPlane];

        // Баннер с дополнительной информацией
        myBanner2Fields["Call"].text = "Позывной: " + myOnePlanePars.Call;
        myBanner2Fields["Icao"].text = "Код ИКАО: " + myOnePlanePars.Icao;
        myBanner2Fields["Model"].text = "Модель ВС: " + myOnePlanePars.Mdl;
        myBanner2Fields["Oper"].text = "Оператор: " + myOnePlanePars.Op;
        myBanner2Fields["From"].text = "From: " + myOnePlanePars.From;
        myBanner2Fields["To"].text = "To: " + myOnePlanePars.To;

        myBanner2.gameObject.SetActive(true);

    }
}
