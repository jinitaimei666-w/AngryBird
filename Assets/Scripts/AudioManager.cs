using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }
    public AudioClip birdCollsion;
    public AudioClip birdSelect;
    public AudioClip birdFlying;
    public AudioClip[] pigCollsion;
    public AudioClip woodCollsion;
    public AudioClip woodDestroyed;

    private void Awake()
    {
        Instance = this;
    }
    public void PlayBirdCollsion(Vector3 position)
    {
        AudioSource.PlayClipAtPoint(birdCollsion,position,1f);
    }
    public void PlayBirdSelect(Vector3 position)
    {
        AudioSource.PlayClipAtPoint(birdSelect, position, 1f);
    }
    public void PlayBirdFlying(Vector3 position)
    {
        AudioSource.PlayClipAtPoint(birdFlying, position, 1f);
    }
    public void PlayPigCollsion(Vector3 position)
    {
        int randomindex=Random.Range(0, pigCollsion.Length);
        AudioClip ac=pigCollsion[randomindex];
        AudioSource.PlayClipAtPoint(ac, position, 1f);
    }
    public void PlayWoodCollsion(Vector3 position)
    {
        AudioSource.PlayClipAtPoint(woodCollsion, position, 1f);
    }
    public void PlayWoodDestroyed(Vector3 position)
    {
        AudioSource.PlayClipAtPoint(woodDestroyed, position, .3f);
    }

}
