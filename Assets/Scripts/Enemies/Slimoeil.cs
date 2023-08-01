using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using OutlineEffect;

/*
 * ------ Quand le joueur n'est pas d�tect� ------
 * 
 *  Cr�er des bool�ens afin de d�terminer certaines "personnalit�s" des ennemis ?
 *  Par exemple, ils ne vont pas tous avoir le m�me comportement (qui serait emb�tant pour certaines 
 *  int�grations). Certains ennemis vont donc rester fixe jusqu'� d�tecter le joueur. D'autres pourraient
 *  r�aliser des rondes + tourner sur eux-m�mes afin de chercher d'�ventuels intrus.
 *
 * ------ D�tection du joueur & mode attaque ------
 * 
 *  On pourrait imaginer diff�rents modes de d�tection (selon envie de gameplay) : le basique serait un 
 *  syst�me de vue pour l'entit�, si le joueur passe devant lui (+ distance proche) : l'ennemi passe
 *  en mode attaque. Un autre syst�me pourrait �tre une entr�e dans une zone sp�cifique qui d�clenche
 *  l'agressivit� des entit�s. Des groupes d'ennemis pourraient aussi �tre form�s : quand un ennemi d�tecte,
 *  il en informe les autres qui vont aussi attaquer le joueur.
 *  
 * ------ Syst�me d'attaque et de poursuite ------
 *
 *  Quand le joueur est d�tect�, une poursuite s'engage. L'entit� cherche � atteindre une position proche de
 *  celle du joueur afin de pouvoir l'attaquer : quand l'entit� atteint une distance suffisante du joueur, elle
 *  va lancer une attaque (d�pend de l'entit� actuelle). Certains ennemis peuvent aussi attaquer � distance (le
 *  syst�me de retour en mode passif en est affect�).
 *  
 *  Les attaques demandent un temps de pr�paration et sont annonc�es par un effet ou une animation un court 
 *  instant avant d'�tre lanc�es. Toucher un ennemi ne cause pas de d�g�ts. D�clencher la r�ception de d�g�ts sur
 *  le personnage du joueur. Chaque attaque est suivie d'un d�lai avant la suivante.
 *  
 * ------ Retour en mode passif et d�faite ------
 *  
 *  L'entit� abandonne sa poursuite si le joueur est impossible � suivre. Soit :
 *  V�rifier qu'il existe un chemin (navmesh) vers le joueur. V�rifier que l'entit� ne reste pas 
 *  sur place trop longtemps -> check toutes les X secondes si bloqu�. Check la distance avec l'entit�.
 *  
 */

public delegate void EnemyImpact(Vector3 impactPoint, bool isWallImpact);
public delegate void Pouf(Vector3 poufPosition);
public delegate void EnemyDamageReceived(Vector3 damagesPoint);

[RequireComponent(typeof(CanSee))]
public class Slimoeil : MonoBehaviour
{
    private GrabbableTarget grabbableBehavior;

    //Si l'entit� peut se d�placer selon son propre d�sir ou subit un d�placement forc�.
    [HideInInspector] public bool canFreelyMove;

    public static EnemyImpact OnEnemyImpact;
    public static Pouf OnPouf;
    public static EnemyDamageReceived OnDamagesReceived;

    [Header("Movements")]
    public bool falling; //Chute de l'entit� : Incapacit� durant cette p�riode.
    public bool grounded;
    [SerializeField] private float heightOffset;
    private float fallTime;
    private const float TimeBeforeDyingByFall = 6f;

    [Header("IA")]
    [SerializeField] private NavMeshAgent agent;
    [SerializeField, Tooltip("Select the behaviors for this enemy. Remember to make the probability sum equal to 100%")]
    private List<EnemyStateCharacteristics> thisEnemyStates;
    [SerializeField] private EnemyAIState currentState;
    [SerializeField] private float minChangeTime, maxChangeTime;
    public float changeTime;
    [SerializeField] private float minWanderingDistance;
    [SerializeField] private float maxWanderingDistance;
    private Vector3 initialPosition;
    [SerializeField] private Transform searchingPointsParent;
    private List<Vector3> searchingPoints = new List<Vector3>();
    int patrolIndex;
    Coroutine distanceCheckCoroutine;
    bool destinationReached;

    [Header("Life, Attack & Pursuit")]
    public bool attackMode;
    private Transform playerTr;
    [SerializeField] private Outline enemyOutline;
    private float damageOutlineVisibleTime;
    [SerializeField] private CanSee canSee;
    [SerializeField] private float minAttackDist;
    private bool attacking;
    private bool lowConfusion;
    private float timeBfrAtk;
    private float timeBfrNewAttack;
    [SerializeField] private ExpressionsScript expressionsManager;
    [SerializeField] private LayerMask playerMask;
    [HideInInspector] public int currentLife;
    [SerializeField] private int maxLife;
    private bool isAlive = true;
    private float immuneTime;
    [SerializeField] private float immuneTimeOnDamage;
    [SerializeField] private GameObject ghostSlimePrefab;

    [Header("Animations & Effects")]
    public Animator enemyAnimator;
    [SerializeField] private ParticleSystem projectionParticles;
    [SerializeField] private ParticleSystem confusedParticles;
    [SerializeField] private ParticleSystem nervousParticles;
    private float confusedTime;
    [SerializeField] private SkinnedMeshRenderer slimoeilRenderer;
    [SerializeField] private AnimationCurve flashHitCurve;
    [SerializeField] private ParticleSystem gonnaAttackParticles;

    [Header("Bounce")]
    [SerializeField] private AnimationCurve bounceCurve;
    Coroutine bounceWhileCrushedCoroutine;
    private float bounceTime = .7f;
    private Transform enemyBody;
    [SerializeField] private BouncyObject bouncyObject;

    [Header("Grabbred & Thrown")]
    [SerializeField] private bool throwing;
    private bool grabbed;
    private Vector3 throwDirection;
    [SerializeField] private float projectionTime = 1.5f;
    [SerializeField] private float projectionDistance = 5f;
    [SerializeField] private float fallSpeed = 3f;
    [SerializeField] private AnimationCurve projectionCurve;
    [SerializeField] private AnimationCurve projectionRotCurve;
    [SerializeField] private AnimationCurve fallCurve;
    [SerializeField] private float entityRadius;
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private LayerMask obstacleMaskWithoutPlayer;

    // Start is called before the first frame update
    void Start()
    {
        enemyAnimator.SetBool("NormalState", true);
        initialPosition = transform.position;
        playerTr = HeroMovements.PlayerBody;
        canFreelyMove = true;

        if (searchingPointsParent)
        {
            foreach (Transform tr in searchingPointsParent)
            {
                searchingPoints.Add(tr.position);
            }
        }

        currentLife = maxLife;
        isAlive = true;
    }

    private void OnDisable()
    {
        slimoeilRenderer.material.SetFloat("LerpToFlash", 0f);
    }

    private void OnDestroy()
    {
        if(!isAlive) Destroy(Instantiate(ghostSlimePrefab, transform.position, transform.rotation), 1.99f);
    }

    // Update is called once per frame
    void Update()
    {
        if (!isAlive) return;

        if (confusedTime > 0f || !canFreelyMove || grabbed || !agent.enabled) goto SkipIA;

        if (!attackMode)
        {
            changeTime -= Time.deltaTime;
            if (changeTime < 0)
            {
                /*currentState = (EnemyAIState)Random.Range(0, thisEnemyStates.Count);
                EnemyStateChange();
                changeTime = Random.Range(minChangeTime, maxChangeTime);*/

                int nextStateValue = Random.Range(0, 100);
                float currentCheckValue = 0;
                foreach (EnemyStateCharacteristics enemyState in thisEnemyStates)
                {
                    if (currentCheckValue + enemyState.probability >= nextStateValue)
                    {
                        currentState = enemyState.aIState;
                        //Debug.Log("Next value is : " + nextStateValue + ", State : " + enemyState.aIState.ToString());
                        changeTime = Random.Range(minChangeTime, maxChangeTime);
                        EnemyStateChange();
                        break;
                    }
                    else
                    {
                        currentCheckValue += enemyState.probability;
                    }
                }
            }

            switch (currentState)
            {
                case EnemyAIState.Idle:
                    agent.destination = transform.position;

                    //Le bool�en est true uniquement quand on transitionne � partir d'un autre �tat non fini.
                    if (destinationReached)
                    {
                        if (distanceCheckCoroutine != null) StopCoroutine(distanceCheckCoroutine);
                        enemyAnimator.SetBool("Walking", false);
                        enemyAnimator.SetBool("NormalState", true);
                        destinationReached = false;
                    }
                    break;

                case EnemyAIState.Wandering:
                    if (destinationReached)
                    {
                        StopCoroutine(distanceCheckCoroutine);
                        currentState = EnemyAIState.Idle;
                    }
                    break;

                case EnemyAIState.Searching:
                    if (destinationReached)
                    {
                        patrolIndex++;
                        if (patrolIndex >= searchingPoints.Count) patrolIndex = 0;
                        agent.destination = searchingPoints[patrolIndex];
                        destinationReached = false;
                    }
                    break;

                case EnemyAIState.StepBackToInitialPos:
                    if (destinationReached)
                    {
                        StopCoroutine(distanceCheckCoroutine);
                        currentState = EnemyAIState.Idle;
                    }
                    break;
            }
        }
        else //Si le joueur est d�tect�
        {
            if(timeBfrNewAttack > 0f)
            {
                timeBfrNewAttack -= Time.deltaTime;
                if (timeBfrNewAttack <= 0f)
                {
                    enemyAnimator.SetBool("Walking", true);
                    enemyAnimator.SetBool("NormalState", false);
                }

                goto SkipIA;
            }

            if(canSee.distToPlayer > minAttackDist && !attacking)
            {
                /*
                 * Ici, ajouter suivi & les diff�rentes situations o� l'entit� va abandonner la poursuite du joueur.
                 * L'entit� abandonne sa poursuite si le joueur est impossible � suivre. Soit :
                 *  V�rifier qu'il existe un chemin (navmesh) vers le joueur. V�rifier que l'entit� ne reste pas 
                 *  sur place trop longtemps -> check toutes les X secondes si bloqu�. Check la distance avec l'entit�.
                 *  
                 *  Mais aussi : compter le nb de secondes durant lesquelles le joueur n'est plus en vue.
                 *  Si le joueur n'est plus visible quelques secondes, la poursuite s'arr�te.
                 */

                if (canSee.targetLostTime < 4f)
                {
                    agent.destination = playerTr.position;
                }
                else
                {
                    agent.destination = transform.position;
                }

                if(agent.remainingDistance <= .5f && canSee.targetLostTime >= 4f)
                {
                    enemyAnimator.SetBool("Walking", false);
                    enemyAnimator.SetBool("NormalState", false);
                    enemyAnimator.SetBool("Searching", true);
                }

                //Si l'entit� n'a pas vu sa cible depuis X sec ou si la cible est trop loin, abandon de la poursuite.
                if(canSee.targetLostTime >= 10f || canSee.distToPlayer > 20f)
                {
                    TargetLost();
                }
                
            }
            else if(!attacking && HeroMovements.grounded)
            {
                //Annoncer l'attaque puis la lancer quelques instants apr�s.
                attacking = true;
                LookAt.LookWithoutYAxis(transform, playerTr.position);
                gonnaAttackParticles.Play();
                timeBfrAtk = .5f;
                agent.destination = transform.position;
            }
            else if(attacking)
            {
                if (timeBfrAtk >= 0f)
                {
                    timeBfrAtk -= Time.deltaTime;
                    if (timeBfrAtk < 0f)
                    {
                        //Lancement r�el de l'attaque, infliger des d�g�ts si joueur touch�.
                        Attack();
                    }
                }
            }
        }

    SkipIA:

        if (confusedTime > 0f && !throwing && !falling)
        {
            confusedTime -= Time.deltaTime;
            if (confusedTime <= 0f)
            {
                canFreelyMove = true;
                agent.enabled = true;

                enemyAnimator.SetBool("Confusing", false);
                enemyAnimator.SetBool("NormalState", true);
                enemyAnimator.SetBool("Searching", false);
                
                enemyAnimator.ResetTrigger("Attack");
                attacking = false;

                //Si l'entit� �tait en mode attaque, elle oublie sa cible apr�s la p�riode de confusion
                if (attackMode && !lowConfusion)
                {
                    TargetLost();
                } 
                else if (attackMode)
                {
                    enemyAnimator.SetBool("Walking", true);
                    enemyAnimator.SetBool("NormalState", false);
                }
                lowConfusion = false;

                if (confusedParticles.isPlaying)
                {
                    confusedParticles.Stop();
                    confusedParticles.Clear();
                }

                if (grabbableBehavior) grabbableBehavior.enabled = true;
                if (grabbableBehavior) grabbableBehavior.grabVisibility.enabled = true;
            }
        }

        //D�sactiver l'outline quelques secondes apr�s avoir subit des d�g�ts.
        if (damageOutlineVisibleTime > 0f)
        {
            damageOutlineVisibleTime -= Time.deltaTime;
            if (damageOutlineVisibleTime <= 0f)
            {
                enemyOutline.enabled = false;
            }
        }

        if (immuneTime > 0f) immuneTime -= Time.deltaTime;

        if (fallTime >= TimeBeforeDyingByFall && isAlive)
        {
            Death();
        }
    }

    private void EnemyStateChange()
    {
        ResetAllStates();

        switch (currentState)
        {
            case EnemyAIState.Idle:
                enemyAnimator.SetBool("Walking", false);
                enemyAnimator.SetBool("NormalState", true);
                break;

            case EnemyAIState.Wandering:
                agent.destination = transform.position +
                    Quaternion.AngleAxis(
                        Random.Range(0, 360),
                        Vector3.up
                    ) * Vector3.forward * Random.Range(minWanderingDistance, maxWanderingDistance);
                enemyAnimator.SetBool("Walking", true);
                enemyAnimator.SetBool("NormalState", false);
                distanceCheckCoroutine = StartCoroutine(DistanceCheck(1f));
                break;

            case EnemyAIState.Searching:
                if (searchingPoints.Count < 1)
                {
                    Debug.LogError("There isn't any searching points.", gameObject);
                    break;
                }

                changeTime += 10f;

                /*int searchDestination = Random.Range(0, searchingPoints.Count);
                agent.destination = searchingPoints[searchDestination];*/

                agent.destination = searchingPoints[patrolIndex];
                distanceCheckCoroutine = StartCoroutine(DistanceCheck(0.5f));
                enemyAnimator.SetBool("Walking", true);
                enemyAnimator.SetBool("NormalState", false);
                break;

            case EnemyAIState.StepBackToInitialPos:
                agent.destination = initialPosition;
                enemyAnimator.SetBool("Walking", true);
                enemyAnimator.SetBool("NormalState", false);
                distanceCheckCoroutine = StartCoroutine(DistanceCheck(1f));
                break;
        }
    }

    /// <summary>
    /// Ici sont reset toutes les valeurs sp�cifiques aux �tats pr�c�dents.
    /// </summary>
    private void ResetAllStates()
    {
        destinationReached = false;
        if (distanceCheckCoroutine != null) StopCoroutine(distanceCheckCoroutine);
    }

    public void BounceBack()
    {
        if (bounceWhileCrushedCoroutine != null) StopCoroutine(bounceWhileCrushedCoroutine);
        if (OnEnemyImpact != null) OnEnemyImpact(transform.position + Vector3.up * heightOffset, false);
        DamagesReceived(transform.position + Vector3.up * heightOffset);
        bounceWhileCrushedCoroutine = StartCoroutine(BounceBounce());
    }

    IEnumerator BounceBounce()
    {
        if (!enemyBody) enemyBody = enemyAnimator.transform;

        float startTime = Time.time;
        float fractionOfJourney;
        Vector3 baseScale = Vector3.one;

        do
        {
            fractionOfJourney = (Time.time - startTime) / bounceTime;
            enemyBody.localScale = baseScale
                + Vector3.up * bounceCurve.Evaluate(fractionOfJourney)
                + Vector3.right * bounceCurve.Evaluate(fractionOfJourney) * 0.2f
                + Vector3.forward * bounceCurve.Evaluate(fractionOfJourney) * 0.2f;

            yield return null;
        } while (fractionOfJourney < 1f);
    }

    IEnumerator DistanceCheck(float checkFrequency)
    {
        do
        {
            float distance = (agent.destination - transform.position).sqrMagnitude;
            if (distance < 1f) //should be : "dist < distRequired*distRequired", but it's one.
            {
                destinationReached = true;
            }
            yield return new WaitForSeconds(checkFrequency);
        } while (canFreelyMove && !attackMode);

    }

    private void OnDrawGizmos()
    {
        switch (currentState)
        {
            case EnemyAIState.Idle:
                Gizmos.color = Color.yellow;
                break;

            case EnemyAIState.Wandering:
                Gizmos.color = Color.green;
                break;

            case EnemyAIState.Searching:
                Gizmos.color = Color.blue;
                break;

            case EnemyAIState.StepBackToInitialPos:
                Gizmos.color = Color.black;
                break;
        }

        if (attackMode) Gizmos.color = Color.red;

        Gizmos.DrawCube(transform.position + Vector3.up, Vector3.one / 2f);
    }

    #region Projection & Fall

    bool agentWasActive = true;
    public void Grabbed(bool grabState)
    {
        grabbed = grabState;
        if (grabbed)
        {
            agentWasActive = agent.enabled;
            agent.enabled = false;
        }
        else if (!throwing && !falling)
        {
            agent.enabled = agentWasActive;
        }
    }

    private List<Transform> directionnalArrows = new List<Transform>();
    private float throwByDashTime = 1.1f;
    private float projectionByDashSpeed = 16f;
    public AnimationCurve decelerationCurve /*= AnimationCurve.Linear(0f, 1f, 1f, 0f)*/;
    Coroutine projectionCoroutine;

    public bool ThrownByPlayer(GrabbableTarget myGrabbableBehavior, ThrowAxis axisThrowDirection)
    {
        //R�aliser des projections (raycast) pour savoir si l'entit� peut �tre lanc�e dans une telle direction.
        //Si oui, on retourne vrai, faux sinon.

        grabbableBehavior = myGrabbableBehavior;

        if (directionnalArrows.Count < 1)
        {
            foreach (Transform tr in myGrabbableBehavior.directionnalArrowParent)
            {
                directionnalArrows.Add(tr);
            }
        }

        switch (axisThrowDirection)
        {
            case ThrowAxis.Right:
                throwDirection = directionnalArrows[0].GetChild(0).position - transform.position;
                GrabAndProjectSkill.LastThrowDir = throwDirection;
                break;

            case ThrowAxis.Left:
                throwDirection = directionnalArrows[1].GetChild(0).position - transform.position;
                GrabAndProjectSkill.LastThrowDir = throwDirection;
                break;

            case ThrowAxis.Backward:
                if (directionnalArrows.Count > 2)
                {
                    throwDirection = directionnalArrows[2].GetChild(0).position - transform.position;
                    GrabAndProjectSkill.LastThrowDir = throwDirection;
                }
                else return false; //Si aucune fl�che n'oriente dans cette direction, on ne projette pas l'entit�.
                break;

            case ThrowAxis.Bottom:
                if (directionnalArrows.Count > 3)
                {
                    throwDirection = directionnalArrows[3].GetChild(0).position - transform.position;
                    GrabAndProjectSkill.LastThrowDir = throwDirection;
                }
                else return false; //Si aucune fl�che n'oriente dans cette direction, on ne projette pas l'entit�.
                break;
        }

        //Faire en sorte que l'ennemi regarde le joueur lorsqu'il est projet�.
        Vector3 cameraPosition = Camera.main.transform.position;
        cameraPosition.y = transform.position.y;
        transform.LookAt(cameraPosition);

        Debug.DrawLine(transform.position, transform.position + throwDirection, Color.green, 5f);

        //StartCoroutine(Throw(axisThrowDirection));

        string animNameForProjection = "";
        switch (axisThrowDirection)
        {
            case ThrowAxis.Right:
                animNameForProjection = "Projected";
                break;

            case ThrowAxis.Left:
                animNameForProjection = "Projected";
                break;

            case ThrowAxis.Backward:
                animNameForProjection = "Projected";
                break;

            case ThrowAxis.Bottom:
                animNameForProjection = "Projected";
                break;
        }
        projectionCoroutine = StartCoroutine(ThrowAndFall(
            throwDirection.normalized,
            true,
            animNameForProjection,
            projectionTime - .2f,
            15.3f
            ));

        return true;
    }

    public void HitByPlayerDash(Vector3 playerDashDirection)
    {
        if (!falling && !throwing)
        {
            //StartCoroutine(ThrownByDash(playerDashDirection));
            projectionCoroutine = StartCoroutine(ThrowAndFall(playerDashDirection, false, "Projected", throwByDashTime, projectionByDashSpeed));
            if (OnEnemyImpact != null) OnEnemyImpact(transform.position, false);
            DamagesReceived(transform.position);
            if (playerTr.parent.TryGetComponent(out HeroMovements heroMovements))
            {
                heroMovements.StopDash();
                Debug.Log("STOP DASH");
            }
        }
    }

    IEnumerator ThrowAndFall(
        Vector3 direction, bool withThrowAxis, string throwAnimationName, float throwTime, float speed
        )
    {
        throwing = true;
        falling = false;

        /* Animations & movements settings */
        if (grabbableBehavior) grabbableBehavior.enabled = false;
        if (grabbableBehavior) grabbableBehavior.grabVisibility.enabled = false;
        canFreelyMove = false;
        agent.enabled = false;
        enemyAnimator.SetBool("NormalState", false);
        enemyAnimator.SetBool("Walking", false);
        enemyAnimator.SetBool("Confusing", false);
        enemyAnimator.SetBool(throwAnimationName, true); //Faire attention au nom renseign�. (Vertical ou Horizontal)
        //Orienter les particules pour qu'elles laissent une tra�n�e en direction inverse de notre destination.
        if (!projectionParticles.isPlaying) projectionParticles.Play();
        projectionParticles.transform.LookAt(transform.position + direction);
        projectionParticles.transform.localRotation *= Quaternion.Euler(0, 180f, 0);

        bouncyObject.enabled = false;
        gameObject.layer = 2;

        /* Param�tres de la projection. */
        if (!withThrowAxis)
        {
            direction += Vector3.up; //Ajout d'un mouvement vers le haut pour r�aliser une courbe montante au d�but.
        }
        float startTime = Time.time;
        float fractionOfJourney;
        Vector3 nextPositionDir;

        do
        {
            if (!falling) //Lorsqu'on vient d'�tre projet�.
            {
                fractionOfJourney = (Time.time - startTime) / throwTime;
                //Si fractionOfJourney atteint 1, cela veut dire que la vitesse de projection est minimale.
                if (fractionOfJourney > 1f)
                {
                    fractionOfJourney = 1f;
                    falling = true;
                    throwing = false;
                }

                nextPositionDir =
                    (direction * decelerationCurve.Evaluate(fractionOfJourney)
                    -
                    (withThrowAxis ?
                     Vector3.zero :
                    (Vector3.up * (1 - decelerationCurve.Evaluate(fractionOfJourney)))))
                    * Time.deltaTime * speed;

                Ray thrownRay = new Ray(transform.position, nextPositionDir);
                if (!Physics.SphereCast(
                    thrownRay, 0.3f, out RaycastHit throwHit, nextPositionDir.magnitude, obstacleMaskWithoutPlayer))
                {
                    transform.position += nextPositionDir;
                }
                else
                {
                    if (OnEnemyImpact != null) OnEnemyImpact(throwHit.point, true);

                    //Faire un autre raycast pour savoir si l'entit� peut tomber en ligne droite.
                    //Si oui, d�clencher le falling pr�matur�ment.
                    //Si non, quitter la boucle.

                    //Si l'entit� ne touche pas le sol et peut encore tomber.
                    if (!Physics.CheckSphere(transform.position - transform.up, .3f, obstacleMaskWithoutPlayer))
                    {
                        falling = true;
                    }
                    else //l'entit� est coll� � un obstacle sous elle. On quitte la boucle.
                    {
                        falling = false;
                    }

                    throwing = false;
                }
            }
            else //une fois que l'entit� a touch� un mur, le sol ou si la direction est devenue verticale
            {
                fallTime += Time.deltaTime;

                nextPositionDir = Vector3.down * Time.deltaTime * speed;

                Ray fallingRay = new Ray(transform.position, nextPositionDir);
                if (!Physics.SphereCast(fallingRay, 0.3f, out RaycastHit fallingHit, nextPositionDir.magnitude, obstacleMask))
                {
                    transform.position += nextPositionDir;
                }
                else
                {
                    falling = false;
                    fallTime = 0f;
                    if (OnEnemyImpact != null) OnEnemyImpact(fallingHit.point, true);
                }
            }
            yield return null;
        } while (throwing || falling);

        /* Une fois que l'entit� est retomb�e au sol (si morte de sa chute : faire autre chose.) */

        confusedTime = 3f;
        enemyAnimator.SetBool(throwAnimationName, false);
        enemyAnimator.SetBool("Confusing", true);
        if (!confusedParticles.isPlaying) confusedParticles.Play();

        //Si on est assez proche du sol, on y colle l'entit� directement
        Ray ray = new Ray(transform.position, -transform.up);
        if (Physics.Raycast(ray, out RaycastHit hit, 1f, obstacleMask))
        {
            transform.position = hit.point + Vector3.up * heightOffset;
        }

        bouncyObject.enabled = true;
        bouncyObject.canBounce = true;
        gameObject.layer = LayerMask.NameToLayer("Enemy");
    }
    #endregion

    #region Life, Damage & Attacks
    public void DamagesReceived(Vector3 damagesPosition)
    {
        if (immuneTime > 0f || currentLife <= 0) return;

        if (OnDamagesReceived != null) OnDamagesReceived(damagesPosition);

        StartCoroutine(RedFlashing(1f));
        immuneTime = immuneTimeOnDamage;

        currentLife--;
        if (currentLife <= 0) 
        {
            Death();
        }
        else
        {
            lowConfusion = true;
            confusedTime = 1f;
            enemyAnimator.SetBool("NormalState", false);
            enemyAnimator.SetBool("Walking", false);
            enemyAnimator.SetBool("Searching", false);
            enemyAnimator.SetBool("Confusing", true);
            canFreelyMove = false;
            agent.enabled = false;
            if (!confusedParticles.isPlaying) confusedParticles.Play();
        }        
    }

    public void Death()
    {
        canFreelyMove = false;
        agent.enabled = false;
        isAlive = false;
        enemyAnimator.ResetTrigger("Attack");
        enemyAnimator.SetTrigger("Dying");
        Destroy(gameObject, 2f);
    }

    public void TargetDetected()
    {
        if (attackMode || confusedTime > 0f || !canFreelyMove || grabbed || !agent.enabled) return;

        attackMode = true;
        canSee.checkFrequency = 0.1f;
        enemyAnimator.SetBool("Walking", true);
        enemyAnimator.SetBool("NormalState", false);
        enemyAnimator.SetBool("Searching", false);
        enemyAnimator.SetFloat("Speed", 1f);
        agent.speed = 5.5f;
        expressionsManager.MakeExpression(MonsterExpressions.Surprise);
    }

    public void TargetLost()
    {
        canSee.checkFrequency = 0.5f;
        enemyAnimator.SetFloat("Speed", 0f);
        agent.speed = 3.5f;
        attackMode = false;
        enemyAnimator.SetBool("Searching", false);
        enemyAnimator.ResetTrigger("Attack");

        expressionsManager.MakeExpression(MonsterExpressions.Question); //Fait appara�tre un point d'interrogation
    }

    private void Attack()
    {
        enemyAnimator.SetTrigger("Attack");
        
        if(Physics.CheckSphere(transform.position+transform.forward, 1f, playerMask))
        {
            playerTr.SendMessage("ReceiveDamages", 0, SendMessageOptions.DontRequireReceiver); //Ajouter ensuite la valeur des d�g�ts

            //Ajouter des effets � l'impact.
        }

        attacking = false;
        enemyAnimator.SetBool("Walking", false);
        enemyAnimator.SetBool("NormalState", true);
        enemyAnimator.SetBool("Searching", false);

        timeBfrNewAttack = 1.2f;
    }

    IEnumerator RedFlashing(float flashDuration)
    {
        float flashTime = 0f;

        do
        {
            flashTime += Time.deltaTime;
            float lerp = flashHitCurve.Evaluate(flashTime/flashDuration);
            slimoeilRenderer.material.SetFloat("LerpToFlash", lerp);

            yield return null;
        } while (flashTime < flashDuration);

        slimoeilRenderer.material.SetFloat("LerpToFlash", 0f);
    }

    #endregion
}
