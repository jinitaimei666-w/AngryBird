using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BlueBird : Bird
{
    public float splitAngle = 25f;   // 上/下分身偏移角度

    protected override void FlyingSkill()
    {
        base.FlyingSkill();          // 置 isHaveUsedSkill = true，防止重复分裂

        Vector2 forward = rgd.velocity;          // 本鸟当前速度矢量
        float speed = forward.magnitude;

        // 本鸟保持原方向继续前飞；另生成两只分身
        CreateSplitBird(forward, speed, splitAngle);
        CreateSplitBird(forward, speed, -splitAngle);
    }

    private void CreateSplitBird(Vector2 forward, float speed, float angle)
    {
        GameObject clone = Instantiate(gameObject, transform.position, Quaternion.identity);

        Bird cloneBird = clone.GetComponent<Bird>();
        if (cloneBird != null)
        {
            cloneBird.isHaveUsedSkill = true;
            cloneBird.enabled = false;   // 关键：禁用分身的 Bird 生命周期
            // 这样分身不会触发 Start 重置刚体，也不会去消耗 GameManager 的鸟槽位
        }

        // 施加速度：把当前方向绕 Z 轴旋转 angle 度
        Vector3 dir = Quaternion.Euler(0, 0, angle) * forward;

        Rigidbody2D rb = clone.GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.velocity = dir.normalized * speed;

        // 一段时间后自动销毁，避免残留在场景
        Destroy(clone, 4f);
    }
}