using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BlackBird : Bird
{
    public float boomRadius = 2.5f;
    public AudioSource boomAudio;      // 爆炸音效，在 Inspector 里拖入
    public AudioClip boomClip;         // 爆炸音频文件（可选，用 PlayOneShot 播放）

    protected override void FullTimeSkill()
    {
        Debug.Log("黑鸟爆炸触发"); if (boomClip != null)
            AudioSource.PlayClipAtPoint(boomClip, transform.position, 1f);
        else Debug.LogWarning("boomClip 未赋值！");

        Collider2D[] colliders = Physics2D.OverlapCircleAll(transform.position, boomRadius);
        foreach (Collider2D collider in colliders)
        {
            Destructible des = collider.GetComponent<Destructible>();
            if (des != null)
            {
                des.TakeDamage(Int32.MaxValue);
            }
        }
        state = BirdState.WaitToDie;
        LoadNextBird();
    }
}