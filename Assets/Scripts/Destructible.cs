using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class Destructible : MonoBehaviour
{
    public int maxHP = 100;
    private int currentHP;

    public List<Sprite> injuredSpriteList;
    private SpriteRenderer spriteRenderer;
    private GameObject boomPrefab;
    private void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        currentHP = maxHP;
        boomPrefab = Resources.Load<GameObject>("Boom2");
    }
    private void OnCollisionEnter2D(Collision2D collision)
    {
        // 鸟还没发射前，障碍物之间的挤压/落地不算伤害，否则关卡一进场它们就互相撞碎
        if (!GameManager.ShotStarted) return;
        TakeDamage((int)(collision.relativeVelocity.magnitude * 8));
    }
    public void TakeDamage(int damage)
    {
        currentHP-= damage;
        if (currentHP <= 0)
        {
            Dead();
        }
        else
        {
            Sprite beforeSprite = spriteRenderer.sprite;
            int index = (int)((maxHP - currentHP) / (maxHP / (injuredSpriteList.Count + 1.0f))) - 1;
            if (index != -1)
            {
                spriteRenderer.sprite = injuredSpriteList[index];
            }
            if (beforeSprite != spriteRenderer.sprite)
            {
                PlayAudioCollsion();
            }
        }
    }
    protected  virtual void PlayAudioCollsion()
    {
        AudioManager.Instance.PlayWoodCollsion(transform.position);
    } 
    protected virtual void PlayAudioDestroyed()
    {
        AudioManager.Instance.PlayWoodDestroyed(transform.position);
    }
    public virtual void Dead()
    {
        PlayAudioDestroyed();
        GameObject.Instantiate(boomPrefab, transform.position, Quaternion.identity);
        Destroy(gameObject);
    }
}
