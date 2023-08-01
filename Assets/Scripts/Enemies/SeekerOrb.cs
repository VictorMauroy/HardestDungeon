using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SeekerOrb : MonoBehaviour
{
    [HideInInspector] public Transform playerTr;
    private Vector3 targetDirAtLaunch;
    [SerializeField] private float velocity;
    private float lifeTime;
    [SerializeField, Tooltip("From 0 to 1. Where value 1 is a full follow")] 
    private float followPlayerProportion;
    private float playerSizeOffset;
    [SerializeField] private ParticleSystem spawnEffect;
    [SerializeField] private ParticleSystem autoDestrEffectPrefab;

    /*  Cr�er un projectile se d�pla�ant dans une direction donn�e � sa cr�ation mais �tant
     *  attirer vers le joueur jusqu'� un certain point. (Projectile � t�te chercheuse.)
     *  Se d�place vers la direction de base + en partie vers le joueur => Addition des deux vecteurs mais
     *  ils n'ont pas la m�me influence sur le d�placement (Destination d'origine > Position actuelle joueur).
     */

    public void SetOrbTarget(Vector3 targetDestination)
    {
        playerSizeOffset = 1f;
        LookAt.LookWithoutYAxis(spawnEffect.transform, playerTr.position);
        spawnEffect.Play();
        targetDirAtLaunch = (targetDestination - transform.position + Vector3.up * playerSizeOffset).normalized;
        lifeTime = 3f;
    }

    // Update is called once per frame
    void Update()
    {
        if (lifeTime <= 0f) return;

        //D�placer l'orbre vers sa position cible + ajout d'un l�ger suivi du joueur dans la direction.
        transform.position += (
            targetDirAtLaunch * (1-followPlayerProportion)
            + (playerTr.position + Vector3.up * playerSizeOffset - transform.position).normalized * followPlayerProportion
            ) * Time.deltaTime * velocity;

        lifeTime -= Time.deltaTime;
        if(lifeTime <= 0f)
        {
            DestroyOrb();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if(other.tag == "Player" && lifeTime > 0f)
        {
            DestroyOrb();
            playerTr.SendMessage("ReceiveDamages", 0 , SendMessageOptions.DontRequireReceiver); //Ajouter ensuite la valeur des d�g�ts
        }
        else if(lifeTime > 0f && lifeTime < 2.8f) //Vie sup � 2.8 pour �viter qu'il explose sur le seeker.
        {
            DestroyOrb();
        }
    }

    private void DestroyOrb()
    {
        lifeTime = 0f;
        Destroy(Instantiate(autoDestrEffectPrefab, transform.position, Quaternion.identity), 2f);
        Destroy(gameObject);
    }
}
