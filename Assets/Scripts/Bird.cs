using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public enum BirdState
{
    Waiting,
    BeforeShoot,
    AfterShoot,
    WaitToDie
}

public class Bird : MonoBehaviour
{
    public BirdState state= BirdState.BeforeShoot;
    public bool isMouseDown = false;
    public float maxDistance = 2.4f;
    public float flySpeed = 13;

    protected Rigidbody2D rgd;

    public bool isFlying = true;
    public bool isHaveUsedSkill = false;
    private Collider2D collider;
    
    void Start()
    {
        rgd = GetComponent<Rigidbody2D>();
       //OnCollsionEnter2D();
        rgd.bodyType = RigidbodyType2D.Static;
        
        collider = GetComponent<Collider2D>();
    }

 
    void Update()
    {
        switch (state)
        {
            case BirdState.Waiting:
                WaitControl();
                break;
            case BirdState.BeforeShoot:
                MoveControl();
                break;
            case BirdState.AfterShoot:
                StopControl();
                SkillControl();
                break;
            case BirdState.WaitToDie:
                break;
            default:
                break;
        }
    }

    private void WaitControl()
    {

    }
    private void OnMouseDown()
    {
        if(state==BirdState.BeforeShoot&& EventSystem.current.IsPointerOverGameObject()==false)
        {
            isMouseDown = true;
            SlingShot.Instance.StartDraw(transform);
            AudioManager.Instance.PlayBirdSelect(transform.position);
        }
    }
    private void OnMouseUp()
    {
        if (state == BirdState.BeforeShoot && EventSystem.current.IsPointerOverGameObject() == false)
        {
            isMouseDown = false;
            SlingShot.Instance.EndDraw();
            Fly();
        }
    }
    private void MoveControl()
    {
        if (isMouseDown)
        {
            transform.position=GetMousePosition();
        }
    }
    private Vector3 GetMousePosition()
    {
        Vector3 centerPosition = SlingShot.Instance.getCenterPosition();
        Vector3 mp= Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mp.z = 0;
        Vector3 mouseDir = mp-centerPosition;
        float distance = (mouseDir).magnitude;
        if(distance > maxDistance)
        {
            mp = mouseDir.normalized * maxDistance + centerPosition;
        }


        return mp;
    }
    private void Fly()
    {
        rgd.bodyType = RigidbodyType2D.Dynamic;
        rgd.velocity = (SlingShot.Instance.getCenterPosition() - transform.position).normalized*flySpeed;
        state = BirdState.AfterShoot;
        // 第一只鸟出手：放开被冻结的障碍物，并允许它们受伤
        GameManager.ReleaseLevel();
        AudioManager.Instance.PlayBirdFlying(transform.position);
    }
    public void GoStage(Vector3 position)
    {
        state = BirdState.BeforeShoot;
        transform.position = position;
    }
    private void StopControl()
    {
        if(rgd.velocity.magnitude < 0.1f)
        {
            state = BirdState.WaitToDie;
            Invoke("LoadNextBird",1f);
        }
    }
    private void SkillControl()
    {
        if (isHaveUsedSkill) return;
        
        if (isFlying == true&&Input.GetMouseButtonDown(0))
        {
            FlyingSkill();
        }
        if (Input.GetMouseButtonDown(0))
        {
            FullTimeSkill();
        }
    }
    // 供派生类（如分身鸟）设置飞行速度
    public void SetVelocity(Vector2 velocity)
    {
        if (rgd != null)
        {
            rgd.velocity = velocity;
        }
    }
    protected virtual void FlyingSkill()
    {
        isHaveUsedSkill = true;
    }
    protected virtual void FullTimeSkill()
    {
        isHaveUsedSkill = true;
    }
    protected void LoadNextBird()
    {
        Destroy(gameObject);
        GameObject.Instantiate(Resources.Load("Boom2"),transform.position,Quaternion.identity);
        GameManager.Instance.LoadNextBird();
    }
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (state == BirdState.AfterShoot)
        {
            isFlying = false;
        }
        
        if (state==BirdState.AfterShoot&&collision.relativeVelocity.magnitude > 3)
        {
            AudioManager.Instance.PlayBirdCollsion(transform.position);
        }
    }
}
