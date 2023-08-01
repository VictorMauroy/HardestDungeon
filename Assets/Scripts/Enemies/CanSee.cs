using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CanSee : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Transform eyes; //Transform d�terminant la direction dans laquelle regarde l'entit�.
    
    [SerializeField] private float halfAngle; //Moiti� de l'angle. 60 degr�s va dire qu'il y aura 60� vers la droite et la gauche en d�tection.
    [SerializeField] private float maxDist; //Distance max � laquelle nous pouvons d�tecter la cible
    [SerializeField] private LayerMask viewObstacleMask; //Masque d�crivant ce qu'est un obstacle � la d�tection de la cible.
    
    [HideInInspector] public bool isSeingTarget; //Vrai si la cible est visible pour l'entit� & assez proche
    [HideInInspector] public float distToPlayer;
    [HideInInspector] public float checkFrequency;
    [HideInInspector] public bool look;

    //Suivi d'entit�.
    [HideInInspector] public Vector3 lastSeenTargetPosition;
    [HideInInspector] public float targetLostTime;

    // Start is called before the first frame update
    void Start()
    {
        look = true;
        if (checkFrequency <= 0f) checkFrequency = 0.5f;

        StartCoroutine(DistanceToPlayerCheck());
    }

    // Update is called once per frame
    void Update()
    {
        //D�bug une ligne : Rouge si la cible n'est pas dans l'angle de vue devant la cible. 
        //Bleu si elle y est mais trop �loign�e. Vert si tout est r�uni pour la d�tection.
        Debug.DrawLine(
            eyes.position,
            target.position,
            IsInViewCone() ?
                (isNotCovered() ? Color.green : Color.blue) :
                Color.red
        );

        //Si la cible est visible, assez proche et dans un certain angle de vue devant l'entit�, alors elle est d�tect�e.
        if (IsInViewCone() && isNotCovered())
        {
            //Activation uniquement lors du passage en true.
            if (!isSeingTarget) SendMessage("TargetDetected", SendMessageOptions.DontRequireReceiver);
            targetLostTime = 0f;
            lastSeenTargetPosition = target.position;
            isSeingTarget = true;
        }
        else
        {
            isSeingTarget = false;
            targetLostTime += Time.deltaTime;
        }
    }

    /// <summary>
    /// Fonction d�terminant si la cible est assez proche et visible par l'entit� (aucun obstacle entre eux).
    /// </summary>
    /// <returns>Vrai si le joueur est visible, faux sinon</returns>
    bool isNotCovered()
    {
        if (distToPlayer < maxDist)
        {
            return !Physics.Raycast(
                eyes.position,
                target.position - eyes.position,
                distToPlayer,
                viewObstacleMask
            );
        }
        else
        {
            return false;
        }
    }

    /// <summary>
    /// Fonction d�terminant si la cible est dans un certain angle de vue devant l'entit�.
    /// </summary>
    /// <returns>Vrai si la cible est � l'int�rieur de l'angle de vue, faux sinon.</returns>
    bool IsInViewCone()
    {
        return Vector3.Angle(
            eyes.forward,
            target.position - eyes.position
        ) < halfAngle;
    }

    /// <summary>
    /// Verifier en continu la distance entre l'entite et sa cible. Utilisation d'une frequence afin d'optimiser l'utilisation du calcul.
    /// </summary>
    /// <param name="checkFrequency">A quelle frequence verifier la distance.</param>
    /// <returns></returns>
    IEnumerator DistanceToPlayerCheck()
    {
        do
        {
            distToPlayer = (target.position - transform.position).magnitude;
            
            yield return new WaitForSeconds(checkFrequency);
        } while (look);
    }
}
