using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using UnityEngine.Networking.NetworkSystem;

using System;
using Synecdoche;

[RequireComponent(typeof(NetworkTransform))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(AudioSource))]
public class NetworkSpaceship : NetworkBehaviour
{
    public float rotationSpeed = 45.0f;
    public float speed = 2.0f;
    public float maxSpeed = 3.0f;

    public ParticleSystem killParticle;
    public GameObject trailGameobject;
    public GameObject bulletPrefab;

    //Network syncvar
    [SyncVar(hook = "OnScoreChanged")]
    public int score;
    [SyncVar]
    public Color color;
    [SyncVar]
    public string playerName;
    [SyncVar(hook = "OnLifeChanged")]
    public int lifeCount;



    public Vector3 acceleration;




    protected Rigidbody _rigidbody;
    protected Collider _collider;
    protected Text _scoreText;

    protected Button _Tap;

    protected float _rotation = 0;
    protected float _acceleration = 0;

    protected float _shootingTimer = 0;

    protected bool _canControl = true;

    //hard to control WHEN Init is called (networking make order between object spawning non deterministic)
    //so we call init from multiple location (depending on what between spaceship & manager is created first).
    protected bool _wasInit = false;


    [SyncVar]
    public bool initDone = false;
    [SyncVar]
    public int playUnix;

    private int [] sequence = {0, 2, 1};
    private int currentSong = 0;
    public int selector = 0;

    // const short MyBeginMsg = 1002;

    // NetworkClient m_client;

    // public void SendReadyToBeginMessage(int myId)
    // {
    //     var msg = new IntegerMessage(myId);
    //     m_client.Send(MyBeginMsg, msg);
    // }

    // public void Init(NetworkClient client)
    // {
    //     m_client = client;
    //     NetworkServer.RegisterHandler(MyBeginMsg, OnServerReadyToBeginMessage);
    // }

    // void OnServerReadyToBeginMessage(NetworkMessage netMsg)
    // {
    //     var beginMessage = netMsg.ReadMessage<IntegerMessage>();
    //     Debug.Log("received OnServerReadyToBeginMessage " + beginMessage.value);
    // }



    // public Button bpmButton;

    public string timeTap;

    public GameObject playerUI;

    public AudioClip[] audioClips; 
    private AudioSource audioSource;
    bool late = false;

    void Awake()
    {
        //register the spaceship in the gamemanager, that will allow to loop on it.
        NetworkGameManager.sShips.Add(this);
    }

    void Start()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _collider = GetComponent<Collider>();

        Renderer[] rends = GetComponentsInChildren<Renderer>();
        foreach (Renderer r in rends)
            r.material.color = color;

        //We don't want to handle collision on client, so disable collider there
        _collider.enabled = isServer;

        // bpmButton = GameObject.Find("TapBPM").GetComponent<Button>();

        // bpmButton.interactable = true;
        // bpmButton.onClick.RemoveAllListeners();
        // bpmButton.onClick.AddListener(CmdTap);

        if (NetworkGameManager.sInstance != null)
        {//we MAY be awake late (see comment on _wasInit above), so if the instance is already there we init
        late = true;
            Init();
        }
    }

    public void Init()
    {
        if (_wasInit)
            return;
        
        GameObject pUI = Instantiate(playerUI, NetworkGameManager.sInstance.canvas.transform);

        pUI.name = gameObject.name + "UI";

        _scoreText = pUI.transform.GetChild(0).GetComponent<Text>();
        // _scoreText.alignment = TextAnchor.MiddleCenter;
        // _scoreText.font = NetworkGameManager.sInstance.uiScoreFont;
        // _scoreText.resizeTextForBestFit = true;
        // _scoreText.color = color;



        _Tap = pUI.transform.GetChild(1).GetComponent<Button>();
        _Tap.interactable = true;
        _Tap.onClick.RemoveAllListeners();
        _Tap.onClick.AddListener(() => CmdTap());


        if (!isLocalPlayer) pUI.SetActive(false);

        
        for (int i = 0; i < audioClips.Length; i++)
        {
                    audioClips[i].LoadAudioData();

                    while (audioClips[i].loadState == AudioDataLoadState.Loading)
                    {
                        Debug.Log("Loading...");
                    }

        }

        audioSource = GetComponent<AudioSource>();
        // audioSource = gameObject.GetComponent<AudioSource>();
        audioSource.clip = audioClips[sequence[currentSong]];
        audioSource.loop = true;
        audioSource.volume = 1f;

        audioSource.Play();
        Invoke("NextSong", audioClips[0].length);
        _wasInit = true;

        CmdInitDone(true);
        UpdateScoreLifeText();
    }

    void NextSong()
    {
        int selec = 0;
        currentSong++;
        
        selec = (int) Mathf.Floor(UnityEngine.Random.value * audioClips.Length);
        if (currentSong < sequence.Length) selec = currentSong;
         

        audioSource.Stop();

        audioSource.clip = audioClips[sequence[selec]];
        audioSource.loop = true;
        audioSource.volume = 1f;

        audioSource.Play();

        CmdSelec(selec);
        Invoke("NextSong", audioClips[sequence[selec]].length);
    }

    [Command]
    public void CmdSelec(int selec)
    {
        selector = selec;
    }

    void OnDestroy()
    {
        NetworkGameManager.sShips.Remove(this);
    }

    [Client]
    void Update()
    {
        // Debug.Log(gameObject.name + " selec: " + selector);
        // Debug.Log(gameObject.name + " vec3: " + acceleration);
        Debug.Log(gameObject.name + " s:" + selector + "v:" + acceleration + "t:" + Synecdoche.Tempus.GetDateCompact(DateTime.Now));


        if (lifeCount == Tempus.UnixTimeNow())
        {
            // lifeCount = 0;

            // audioSource.clip = audioClips[1];
            // audioSource.loop = true;
            // audioSource.volume = 1f;
            // audioSource.Play();

            // Debug.Log("PLAY!");
        }

        _rotation = 0;
        _acceleration = 0;

        if (!isLocalPlayer || !_canControl)
            return;

        _rotation = Input.GetAxis("Horizontal");

        float x = (float) Math.Round(Input.acceleration.x, 2);
        float y = (float) Math.Round(Input.acceleration.y, 2);
        float z = (float) Math.Round(Input.acceleration.z, 2);

        // SendReadyToBeginMessage((int) x * 100);

        acceleration = new Vector3(x, y, z);
        CmdAcc(acceleration);

        // _acceleration = Input.GetAxis("Vertical");
        _acceleration = acceleration.x;

        if(Input.GetButton("Jump") && _shootingTimer <= 0)
        {
            _shootingTimer = 0.2f;
            //we call a Command, that will be executed only on server, to spawn a new bullet
            //we pass the position&forward to be sure to shoot from the right one (server can lag one frame behind)
            CmdFire(transform.position, transform.forward, _rigidbody.velocity);
        }

        if (_shootingTimer > 0)
            _shootingTimer -= Time.deltaTime;
    }

    [Command]
    public void CmdInitDone(bool done)
    {
        initDone = done;
    }

    [Command]
    public void CmdAcc(Vector3 newValue)
    {
        acceleration = newValue;
    }

    // [ClientCallback]
    void FixedUpdate()
    {
        if (!hasAuthority)
            return;

        if (!_canControl)
        {//if we can't control, mean we're destroyed, so make sure the ship stay in spawn place
            _rigidbody.rotation = Quaternion.identity;
            _rigidbody.position = Vector3.zero;
            _rigidbody.velocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
        }
        else
        {
            Quaternion rotation = _rigidbody.rotation * Quaternion.Euler(0, _rotation * rotationSpeed * Time.fixedDeltaTime, 0);
            //_rigidbody.MoveRotation(rotation);

            //_rigidbody.AddForce((rotation * Vector3.forward) * _acceleration * 1000.0f * speed * Time.deltaTime);

            if (_rigidbody.velocity.magnitude > maxSpeed * 1000.0f)
            {
                _rigidbody.velocity = _rigidbody.velocity.normalized * maxSpeed * 1000.0f;
            }


            CheckExitScreen();
        }
    }

    [ClientCallback]
    void OnCollisionEnter(Collision coll)
    {
        if (isServer)
            return; // hosting client, server path will handle collision

        //if not, we are on a client, so just disable the spaceship (& play destruction aprticle).
        //This way client won't see it's destruction delayed (time for it to happen on server & message to get back)
        NetworkAsteroid asteroid = coll.gameObject.GetComponent<NetworkAsteroid>();

        if(asteroid != null)
        {
            LocalDestroy();
        }
    }

    void CheckExitScreen()
    {
        if (Camera.main == null)
            return;

        if (Mathf.Abs(_rigidbody.position.x) > Camera.main.orthographicSize * Camera.main.aspect)
        {
            _rigidbody.position = new Vector3(-Mathf.Sign(_rigidbody.position.x) * Camera.main.orthographicSize * Camera.main.aspect, 0, _rigidbody.position.z);
            _rigidbody.position -= _rigidbody.position.normalized * 0.1f; // offset a little bit to avoid looping back & forth between the 2 edges 
        }

        if (Mathf.Abs(_rigidbody.position.z) > Camera.main.orthographicSize)
        {
            _rigidbody.position = new Vector3(_rigidbody.position.x , _rigidbody.position.y, -Mathf.Sign(_rigidbody.position.z) * Camera.main.orthographicSize);
            _rigidbody.position -= _rigidbody.position.normalized * 0.1f; // offset a little bit to avoid looping back & forth between the 2 edges 
        }
    }
    [ClientRpc]
    void RpcDamage(int amount)
    {
        //audioSource.Play();
    }

    public void TakeDamage(int amount)
    {
        if (!isServer)
            return;

        RpcDamage(amount);
    }

    // [ClientCallback]
    // void OnGUI()
    // {
    //     Rect box = new Rect(20, 100, 500, 70);
    //     GUIStyle style = new GUIStyle(GUI.skin.box);
    //     style.alignment = TextAnchor.MiddleLeft;
    //     style.fontSize = 50;
        
    //     float x = (float) Math.Round(Input.acceleration.x, 2);
    //     float y = (float) Math.Round(Input.acceleration.y, 2);
    //     float z = (float) Math.Round(Input.acceleration.z, 2);

    //     GUI.TextField(box, x + " / " + y + " / " + z, style);
    // }

    // --- Score & Life management & display
    void OnScoreChanged(int newValue)
    {
        score = newValue;
        UpdateScoreLifeText();
    }

    void OnLifeChanged(int newValue)
    {
        lifeCount = newValue;
        UpdateScoreLifeText();
    }

    // void OnUnixChanged(int newValue)
    // {
    //     playUnix = newValue;
    // }
    // public void Play()
    // {
    //     audioSource.Play();
    // }

    void UpdateScoreLifeText()
    {
        if (_scoreText != null)
        {
            _scoreText.text = "SCORE : " + score;
            _scoreText.text = "Late: " + late;
            _scoreText.text = "lifecount: " + lifeCount + " " + audioSource.isPlaying;

            // _scoreText.text = playerName + "\nSCORE : " + score + "\nLIFE : ";
            // for (int i = 1; i <= lifeCount; ++i)
            //     _scoreText.text += "X";
        }
    }

    //===================================

    //We can't disable the whole object, as it would impair synchronisation/communication
    //So disabling mean disabling collider & renderer only
    public void EnableSpaceShip(bool enable)
    {
        GetComponent<Renderer>().enabled = enable;
        _collider.enabled = isServer && enable;
        trailGameobject.SetActive(enable);

        _canControl = enable;
    }

    [Client]
    public void LocalDestroy()
    {
        killParticle.transform.SetParent(null);
        killParticle.transform.position = transform.position;
        killParticle.gameObject.SetActive(true);
        killParticle.time = 0;
        killParticle.Play();

        if (!_canControl)
            return;//already destroyed, happen if destroyed Locally, Rpc will call that later

        EnableSpaceShip(false);
    }

    //this tell the game this should ONLY be called on server, will ignore call on client & produce a warning
    [Server]
    public void Kill()
    {
        lifeCount -= 1;

        RpcDestroyed();
        EnableSpaceShip(false);

        if (lifeCount > 0)
        {
            //we start the coroutine on the manager, as disabling a gameobject stop ALL coroutine started by it
            NetworkGameManager.sInstance.StartCoroutine(NetworkGameManager.sInstance.WaitForRespawn(this));
        }
    }

    [Server]
    public void Respawn()
    {
        EnableSpaceShip(true);
        RpcRespawn();
    }

    public void CreateBullets()
    {
        Vector3[] vectorBase = { _rigidbody.rotation * Vector3.right, _rigidbody.rotation * Vector3.up, _rigidbody.rotation * Vector3.forward };
        Vector3[] offsets = { -1.5f * vectorBase[0] + -0.5f * vectorBase[2], 1.5f * vectorBase[0] + -0.5f * vectorBase[2] };

        for (int i = 0; i < 2; ++i)
        {
            GameObject bullet = Instantiate(bulletPrefab, _rigidbody.position + offsets[i], Quaternion.identity) as GameObject;
            NetworkSpaceshipBullet bulletScript = bullet.GetComponent<NetworkSpaceshipBullet>();

            bulletScript.originalDirection = vectorBase[2]; 
            bulletScript.owner = this;

            //NetworkServer.SpawnWithClientAuthority(bullet, connectionToClient);
        }
    }

    // =========== NETWORK FUNCTIONS

    [Command]
    public void CmdFire(Vector3 position, Vector3 forward, Vector3 startingVelocity)
    {
        if (!isClient) //avoid to create bullet twice (here & in Rpc call) on hosting client
            CreateBullets();

        RpcFire();
    }

    //
    [Command]
    public void CmdCollideAsteroid()
    {
        Kill();
    }

    [ClientRpc]
    public void RpcFire()
    {
        CreateBullets();
    }


    //called on client when the player die, spawn the particle (this is only cosmetic, no need to do it on server)
    [ClientRpc]
    void RpcDestroyed()
    {
        LocalDestroy();
    }

    [ClientRpc]
    void RpcRespawn()
    {
        EnableSpaceShip(true);

        killParticle.gameObject.SetActive(false);
        killParticle.Stop();
    }


    // public void OnCmdTap()
    // {
    //     CmdTap(Tempus.UnixTimeNow());
    // }


    [Command]
    public void CmdTap()
    {
        timeTap = Tempus.GetDateCompact(DateTime.Now);
    }
}
