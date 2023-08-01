using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using OutlineEffect;

public enum SeekerAnimState
{
    Idle,
    Fly,
    Dizzy,
    Thrown,
    Searching
}

/*
 * -------- Joueur non d�tect� : --------
 * Plusieurs options : 
 * - Rester sur place et chercher aux alentours.
 * - Faire une patrouille entre divers endroits/points.
 * - Se d�placer dans une direction al�atoire.
 * - Revenir � sa position d'origine.
 * 
 * -------- D�tection du joueur : --------
 * Le Seeker d�tecte automatiquement le joueur s'il est � proximit� et qu'il n'est pas cach� derri�re un obstacle.
 * Quelque que soit la direction du joueur : le seeker a des yeux observant tout son environnement.
 * 
 * -------- Joueur d�tect� : --------
 * Passage en mode attaque. Si le joueur est visible -> pr�paration puis lancement de l'attaque � distance.
 * L'attaque � distance g�n�re une orbre qui va l�g�rement suivre le joueur. D�lai entre deux attaques. Si le joueur
 * n'est pas visible, le seeker ne pr�pare pas son attaque et ne la lance pas.
 * Retour en mode passif -> Lorsque le joueur est trop �loign� ou non visible pendant trop longtemps.
 */

public class Seeker : MonoBehaviour
{
    private GrabbableTarget grabbableBehavior;

    //Si l'entit� peut se d�placer selon son propre d�sir ou subit un d�placement forc�.
    [HideInInspector] public bool canFreelyMove;

    public static EnemyImpact OnEnemyImpact;
    public static Pouf OnPouf;
    public static EnemyDamageReceived OnDamagesReceived;

    [Header("Movements")]
    public bool falling; //Chute de l'entit� : Incapacit� durant cette p�riode.
    private float verticalVelocity;
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float gravityScale;
    [SerializeField] private float heightOffset;
    private RaycastHit groundHit;
    private float fallTime;
    private const float TimeBeforeDyingByFall = 6f;

    [Header("Passive behavior")]
    [SerializeField, Tooltip("Select the behaviors for this enemy. Remember to make the probability sum equal to 100%")]
    private List<EnemyStateCharacteristics> thisEnemyStates;
    [SerializeField] private EnemyAIState currentState;
    [SerializeField] private float minChangeTime, maxChangeTime;
    private float changeTime;
    private Vector3 initialPosition;
    private Vector3 currentDestination;
    [SerializeField] private float flySpeed;
    [SerializeField] private float minWanderingDistance;
    [SerializeField] private float maxWanderingDistance;
    [SerializeField] private Transform searchingPointsParent;
    private List<Vector3> searchingPoints = new List<Vector3>();
    int patrolIndex;
    private float heightBeforeFall;
    Coroutine distanceCheckCoroutine;
    Coroutine movementCoroutine;
    private bool destinationReached;
    private float timeBfrNextSearch;

    private bool downForceActivated;
    private const float downForceDuration = .7f;
    private float downForceStartTime;
    [SerializeField] private AnimationCurve jumpOnMeCurve;

    [Header("Agressive behavior")]
    public bool attackMode;
    private Transform playerTr;
    [SerializeField] private GameObject seekerOrbPrefab;
    [SerializeField] private Transform orbInvocationPosition;
    [HideInInspector] public int currentLife;
    [SerializeField] private int maxLife;
    private bool attacking;
    private float timeBfrAtk;
    private float timeBfrNewAttack;
    private Vector3 lastSeenPlayerPos;
    private float targetLostTime;
    [SerializeField] private Outline enemyOutline;
    private float damageOutlineVisibleTime;
    [SerializeField] private float checkFrequency;
    private float distanceToPlayer;
    [SerializeField] private float playerDetectionDist;
    [SerializeField] private ExpressionsScript expressionsManager;
    private bool isAlive;
    private float immuneTime;
    [SerializeField] private float immuneTimeOnDamage;
    [SerializeField] private GameObject ghostSeekerPrefab;

    [Header("Animations & Effects")]
    public Animator seekerAnimator;
    [SerializeField] private Transform enemyBody;
    [SerializeField] private ParticleSystem projectionParticles;
    [SerializeField] private ParticleSystem confusedParticles;
    [SerializeField] private ParticleSystem nervousParticles;
    private float confusedTime;
    //Animations shortcuts
    private int idleAnimIndex;
    private int flyAnimIndex;
    private int dizzyAnimIndex;
    private int thrownAnimIndex;
    private int searchAnimIndex;
    [SerializeField] private SkinnedMeshRenderer seekerRenderer;
    [SerializeField] private AnimationCurve flashHitCurve;
    [SerializeField] private ParticleSystem gonnaAttackParticles;

    [Header("Bounce")]
    [SerializeField] private AnimationCurve bounceCurve;
    Coroutine bounceWhileCrushedCoroutine;
    private float bounceTime = .7f;
    [SerializeField] private BouncyObject bouncyObject;

    [Header("Grabbred & Thrown")]
    [SerializeField] private bool throwingState;
    private bool grabbed;
    private Vector3 throwDirection;
    [SerializeField] private float projectionTime = 1.5f;
    [SerializeField] private float projectionDistance = 5f;
    [SerializeField] private AnimationCurve projectionCurve;
    [SerializeField] private AnimationCurve projectionRotCurve;
    [SerializeField] private float entityRadius;
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private LayerMask obstacleMaskWithoutPlayer;

    // Start is called before the first frame update
    void Start()
    {
        idleAnimIndex = Animator.StringToHash("NormalState");
        flyAnimIndex = Animator.StringToHash("Flying");
        dizzyAnimIndex = Animator.StringToHash("Dizzy");
        thrownAnimIndex = Animator.StringToHash("Projected");
        searchAnimIndex = Animator.StringToHash("Searching");

        ChangeSeekerAnimation(SeekerAnimState.Idle);
        playerTr = HeroMovements.PlayerBody;
        heightBeforeFall = transform.position.y;
        canFreelyMove = true;

        initialPosition = transform.position;
        if (searchingPointsParent)
        {
            foreach (Transform tr in searchingPointsParent)
            {
                searchingPoints.Add(tr.position);
            }
        }

        currentLife = maxLife;
        isAlive = true;
        StartCoroutine(PlayerDistanceCheck(checkFrequency));
    }

    private void OnDisable()
    {
        seekerRenderer.material.SetFloat("LerpToFlash", 0f);
    }

    private void OnDestroy()
    {
        if (!isAlive) Destroy(Instantiate(ghostSeekerPrefab, transform.position, transform.rotation), 1.99f);
    }

    // Update is called once per frame
    void Update()
    {
        if (!isAlive) return;

        if (confusedTime > 0f || !canFreelyMove || grabbed) goto SkipIA;

        if (!attackMode)
        {
            changeTime -= Time.deltaTime;
            if (changeTime < 0)
            {
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
                    currentDestination = transform.position;

                    //Le bool�en est true uniquement quand on transitionne � partir d'un autre �tat non fini.
                    if (destinationReached)
                    {
                        if (distanceCheckCoroutine != null) StopCoroutine(distanceCheckCoroutine);
                        ChangeSeekerAnimation(SeekerAnimState.Idle);
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
                    if (destinationReached && timeBfrNextSearch > 0f)
                    {
                        timeBfrNextSearch -= Time.deltaTime;
                        if(timeBfrNextSearch <= 0f)
                        {
                            patrolIndex++;
                            if (patrolIndex >= searchingPoints.Count) patrolIndex = 0;
                            currentDestination = searchingPoints[patrolIndex];
                            
                            destinationReached = false;
                            ChangeSeekerAnimation(SeekerAnimState.Fly);
                            LookAt.LookWithoutYAxis(enemyBody, currentDestination);
                        }
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

            if(distanceToPlayer <= playerDetectionDist)
            {
                Ray toPlayerRay = new Ray(transform.position, playerTr.position - transform.position);
                if(!Physics.Raycast(toPlayerRay, distanceToPlayer, obstacleMaskWithoutPlayer))
                {
                    lastSeenPlayerPos = playerTr.position;
                    TargetDetected();
                }
            }
        }
        else //Si le joueur est assez proche pour �tre attaqu�
        {
            /* --------Joueur d�tect�: --------
            * Passage en mode attaque. Si le joueur est visible->pr�paration puis lancement de l'attaque � distance.
            * L'attaque � distance g�n�re une orbre qui va l�g�rement suivre le joueur. D�lai entre deux attaques. 
            * Si le joueur n'est pas visible, le seeker ne pr�pare pas son attaque et ne la lance pas.
            * Retour en mode passif->Lorsque le joueur est trop �loign� ou non visible pendant trop longtemps.
            */

            Ray toPlayerRay = new Ray(transform.position, playerTr.position - transform.position);
            if (!Physics.Raycast(toPlayerRay, distanceToPlayer, obstacleMaskWithoutPlayer)
                && distanceToPlayer <= playerDetectionDist)
            {
                lastSeenPlayerPos = playerTr.position;
                enemyBody.LookAt(playerTr.position);
                targetLostTime = 0f;
            }
            else
            {
                targetLostTime += Time.deltaTime;
                if(targetLostTime > 5f && targetLostTime < 10f)
                {
                    if(targetLostTime < 5.3f) ChangeSeekerAnimation(SeekerAnimState.Searching);

                    goto SkipIA;
                } 
                else if (targetLostTime >= 10f)
                {
                    TargetLost();
                    goto SkipIA;
                }
            }

            if (timeBfrNewAttack > 0f)
            {
                timeBfrNewAttack -= Time.deltaTime;
                if (timeBfrNewAttack <= 0f)
                {

                }

                goto SkipIA;
            }

            if (!attacking)
            {
                attacking = true;
                gonnaAttackParticles.Play();
                timeBfrAtk = .5f;
                seekerAnimator.SetTrigger("Attack");
            }
            else if (attacking)
            {
                if (timeBfrAtk >= 0f)
                {
                    timeBfrAtk -= Time.deltaTime;
                    if (timeBfrAtk < 0f)
                    {
                        //Lancement de l'orbre d'attaque
                        Attack();
                    }
                }
            }
        }

    SkipIA:

        if (falling)
        {
            Ray toGroundRay = new Ray(transform.position, -transform.up);

            //Si on rentre en collision avec un objet en-dessous de nous.
            if (Physics.SphereCast(toGroundRay, 0.3f, out groundHit, heightOffset - 0.2f, obstacleMask))
            {
                verticalVelocity = 0f;
                falling = false;
                fallTime = 0f;

                //Lancer une animation d'impact au sol puis redonner apr�s quelques instants la possibilit� de bouger
                // � l'entit�.

                if (OnEnemyImpact != null) OnEnemyImpact(groundHit.point, true);
                confusedTime = 5f;
            }
            else //Si rien n'est touch�, on continue de tomber
            {
                verticalVelocity += gravity * gravityScale * Time.deltaTime;
                fallTime += Time.deltaTime;
                /*
                 * Compteur de temps de chute : au bout d'un certain temps, l'entit� meurt.
                 */
            }

            transform.Translate(verticalVelocity * Vector3.up * Time.deltaTime);
        }

        if (downForceActivated)
        {
            float downForceProgression = (Time.time - downForceStartTime) / downForceDuration;
            transform.position = new Vector3(
                transform.position.x,
                heightBeforeFall + jumpOnMeCurve.Evaluate(downForceProgression),
                transform.position.z
            );
            if (downForceProgression >= 1f) downForceActivated = false;
        }

        if (confusedTime > 0f && !throwingState)
        {
            confusedTime -= Time.deltaTime;
            if (confusedTime <= 0f)
            {
                canFreelyMove = true;
                ChangeSeekerAnimation(SeekerAnimState.Idle);

                if (OnPouf != null) OnPouf(transform.position);

                //Possibilit� de rajouter un check plus propre pour savoir s'il peut retourner � sa position initiale,
                //  en utilisant notamment des raycasts. Au besoin.
                if (Vector3.Distance(transform.position, initialPosition) > 16f)
                {
                    transform.position = initialPosition;
                    if (OnPouf != null) OnPouf(transform.position);
                }
                else
                {
                    if (Physics.Raycast(transform.position, -transform.up, 1f, obstacleMask))
                    {
                        ChangeSeekerAnimation(SeekerAnimState.Fly);

                        if (Physics.Raycast(transform.position, transform.up, out RaycastHit heightHit, 3f, obstacleMask))
                        {
                            transform.position = transform.position + Vector3.up * (heightHit.distance - 0.8f);
                        }
                        else
                        {
                            transform.position = transform.position + Vector3.up * 3f;
                        }
                        if (OnPouf != null) OnPouf(transform.position);
                    }
                }

                if (!attackMode && currentState != EnemyAIState.Idle)
                {
                    MoveAtWill(true);
                } 
                else if (attackMode)
                {
                    distanceCheckCoroutine = StartCoroutine(DistanceCheck(checkFrequency));
                }

                if (confusedParticles.isPlaying)
                {
                    confusedParticles.Stop();
                    confusedParticles.Clear();
                }
                if (grabbableBehavior)
                {
                    grabbableBehavior.enabled = true;
                    grabbableBehavior.grabVisibility.enabled = true;
                }

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

    #region Passive Behavior
    private void EnemyStateChange()
    {
        ResetAllStates();

        switch (currentState)
        {
            case EnemyAIState.Idle:
                ChangeSeekerAnimation(SeekerAnimState.Idle);
                break;

            case EnemyAIState.Wandering:
                currentDestination = initialPosition +
                    Quaternion.AngleAxis(
                        Random.Range(0, 360),
                        Vector3.up
                    ) * Vector3.forward * Random.Range(minWanderingDistance, maxWanderingDistance);

                movementCoroutine = StartCoroutine(MoveToPoint());
                break;

            case EnemyAIState.Searching:
                if (searchingPoints.Count < 1)
                {
                    Debug.LogError("There isn't any searching points.", gameObject);
                    break;
                }

                changeTime += 15f;
                /*int searchDestination = Random.Range(0, searchingPoints.Count);
                agent.destination = searchingPoints[searchDestination];*/

                currentDestination = searchingPoints[patrolIndex];
                movementCoroutine = StartCoroutine(MoveToPoint());
                break;

            case EnemyAIState.StepBackToInitialPos:
                currentDestination = initialPosition;
                movementCoroutine = StartCoroutine(MoveToPoint());
                break;
        }
    }

    /// <summary>
    /// Ici sont reset toutes les valeurs sp�cifiques aux �tats pr�c�dents.
    /// </summary>
    private void ResetAllStates()
    {
        destinationReached = false;
        MoveAtWill(false);
        transform.rotation *= Quaternion.AngleAxis(-transform.rotation.eulerAngles.x, Vector3.right);
    }

    IEnumerator MoveToPoint()
    {
        float distCheckFreq = 1f;
        switch (currentState)
        {
            case EnemyAIState.Wandering:
                distCheckFreq = 1f;
                break;
            case EnemyAIState.Searching:
                distCheckFreq = 0.5f;
                break;
            case EnemyAIState.StepBackToInitialPos:
                distCheckFreq = 0.3f;
                break;
        }
        distanceCheckCoroutine = StartCoroutine(DistanceCheck(distCheckFreq));
        
        ChangeSeekerAnimation(SeekerAnimState.Fly);
        LookAt.LookWithoutYAxis(enemyBody, currentDestination);

        Vector3 nextPosition;

        do
        {
            if (!destinationReached)
            {
                nextPosition = (currentDestination - transform.position).normalized * Time.deltaTime * flySpeed;
                Ray toDestRay = new Ray(transform.position, nextPosition);
                if (!Physics.SphereCast(toDestRay, entityRadius, entityRadius + 0.2f, obstacleMask))
                {
                    transform.position += nextPosition;
                }
            }

            yield return null;
        } while (canFreelyMove && currentState != EnemyAIState.Idle && !grabbed && !attackMode);

        //Reset la rotation en X � 0.
        transform.rotation *= Quaternion.AngleAxis(-transform.rotation.eulerAngles.x, Vector3.right);
    }

    /// <summary>
    /// Fonction � appeler lorsque l'entit� ne peut plus bouger selon son propre d�sir.
    /// </summary>
    void MoveAtWill(bool canMove)
    {
        if (canMove)
        {
            if (movementCoroutine != null) StopCoroutine(movementCoroutine);
            if (distanceCheckCoroutine != null) StopCoroutine(distanceCheckCoroutine);
        }
        else
        {
            movementCoroutine = StartCoroutine(MoveToPoint());
        }
    }

    private void ChangeSeekerAnimation(SeekerAnimState newSeekerAnimState)
    {
        switch (newSeekerAnimState)
        {
            case SeekerAnimState.Idle:
                seekerAnimator.SetBool(idleAnimIndex, true);
                seekerAnimator.SetBool(flyAnimIndex, false);
                seekerAnimator.SetBool(dizzyAnimIndex, false);
                seekerAnimator.SetBool(thrownAnimIndex, false);
                seekerAnimator.SetBool(searchAnimIndex, false);
                break;

            case SeekerAnimState.Fly:
                seekerAnimator.SetBool(idleAnimIndex, false);
                seekerAnimator.SetBool(flyAnimIndex, true);
                seekerAnimator.SetBool(dizzyAnimIndex, false);
                seekerAnimator.SetBool(thrownAnimIndex, false);
                seekerAnimator.SetBool(searchAnimIndex, false);
                break;

            case SeekerAnimState.Dizzy:
                seekerAnimator.SetBool(idleAnimIndex, false);
                seekerAnimator.SetBool(flyAnimIndex, false);
                seekerAnimator.SetBool(dizzyAnimIndex, true);
                seekerAnimator.SetBool(thrownAnimIndex, false);
                seekerAnimator.SetBool(searchAnimIndex, false);
                break;

            case SeekerAnimState.Thrown:
                seekerAnimator.SetBool(idleAnimIndex, false);
                seekerAnimator.SetBool(flyAnimIndex, false);
                seekerAnimator.SetBool(dizzyAnimIndex, false);
                seekerAnimator.SetBool(thrownAnimIndex, true);
                seekerAnimator.SetBool(searchAnimIndex, false);
                break;

            case SeekerAnimState.Searching:
                seekerAnimator.SetBool(idleAnimIndex, false);
                seekerAnimator.SetBool(flyAnimIndex, false);
                seekerAnimator.SetBool(dizzyAnimIndex, false);
                seekerAnimator.SetBool(thrownAnimIndex, false);
                seekerAnimator.SetBool(searchAnimIndex, true);
                break;
        }
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

    #endregion

    public void Grabbed(bool grabState)
    {
        grabbed = grabState;
    }

    public void BounceBack()
    {
        if (bounceWhileCrushedCoroutine != null) StopCoroutine(bounceWhileCrushedCoroutine);
        if (OnEnemyImpact != null) OnEnemyImpact(transform.position + Vector3.up * heightOffset, false);
        DamagesReceived(transform.position + Vector3.up * heightOffset);
        bounceWhileCrushedCoroutine = StartCoroutine(BounceBounce());

        downForceActivated = true;
        downForceStartTime = Time.time;
        heightBeforeFall = transform.position.y;
    }

    IEnumerator BounceBounce()
    {
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

    #region Projection by player spell or dash

    private List<Transform> directionnalArrows = new List<Transform>();
    private bool obstacleMeet;
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
        enemyBody.LookAt(cameraPosition);

        Debug.DrawLine(transform.position, transform.position + throwDirection, Color.green, 5f);

        /*string animNameForProjection = "";
        switch (axisThrowDirection)
        {
            case ThrowAxis.Right:
                animNameForProjection = "projectedH";
                break;

            case ThrowAxis.Left:
                animNameForProjection = "projectedH";
                break;

            case ThrowAxis.Backward:
                animNameForProjection = "projectedV";
                break;

            case ThrowAxis.Bottom:
                animNameForProjection = "projectedV";
                break;
        }


        projectionCoroutine = StartCoroutine(ThrowToDirection(
            throwDirection.normalized,
            true,
            animNameForProjection,
            projectionTime - .2f,
            15.3f
            )); */

        projectionCoroutine = StartCoroutine(ThrowByPower(axisThrowDirection));
        MoveAtWill(false);

        return true;
    }

    IEnumerator ThrowByPower(ThrowAxis axisThrowDirection)
    {
        throwingState = true;
        obstacleMeet = false;
        canFreelyMove = false;
        grabbableBehavior.enabled = false;
        grabbableBehavior.grabVisibility.enabled = false;

        heightBeforeFall = transform.position.y;

        //D�clenchement des animations et effets de projection.
        ChangeSeekerAnimation(SeekerAnimState.Thrown);
        /*switch (axisThrowDirection)
        {
            case ThrowAxis.Right:
                seekerAnimator.SetBool("projectedH", true);
                break;

            case ThrowAxis.Left:
                seekerAnimator.SetBool("projectedH", true);
                break;

            case ThrowAxis.Backward:
                seekerAnimator.SetBool("projectedV", true);
                break;

            case ThrowAxis.Bottom:
                seekerAnimator.SetBool("projectedV", true);
                break;
        }*/

        //Orienter les particules pour qu'elles laissent une tra�n�e en direction inversion de notre destination.
        if (!projectionParticles.isPlaying) projectionParticles.Play();
        projectionParticles.transform.LookAt(transform.position + throwDirection);
        projectionParticles.transform.localRotation *= Quaternion.Euler(0, 180f, 0);

        //Param�tres de la projection.
        float startTime = Time.time;
        float fractionOfJourney;
        Vector3 startPosition = transform.position;
        Vector3 endPosition = startPosition + throwDirection * projectionDistance;
        Vector3 obstaclePosition = Vector3.zero;
        //seekerAnimator.SetFloat("RotationSpeed", 1f);

        /* Ci-dessous : d�placement graduel de l'entit� vers sa destination de projection.
         * R�alisation de Raycast � chaque d�placement afin de stopper pr�matur�ment si l'entit� rencontre un mur. */
        do
        {
            fractionOfJourney = (Time.time - startTime) / projectionTime;

            Vector3 nextPosition = Vector3.Lerp(
                startPosition,
                endPosition,
                projectionCurve.Evaluate(fractionOfJourney)
                );
            //seekerAnimator.SetFloat("RotationSpeed", projectionRotCurve.Evaluate(fractionOfJourney));

            if (!Physics.CheckSphere(nextPosition, entityRadius, obstacleMask))
            {
                transform.position = nextPosition;
            }
            else
            {
                Ray ray = new Ray(transform.position, throwDirection);
                float rayDist = (transform.position - nextPosition).magnitude;
                if (Physics.SphereCast(ray, entityRadius, out RaycastHit hit, rayDist, obstacleMask))
                {
                    obstaclePosition = hit.point;
                }

                obstacleMeet = true;
            }

            yield return null;
        } while (!obstacleMeet && fractionOfJourney < 1f);

        //Effets et animations de fin de projection 
        /*switch (axisThrowDirection)
        {
            case ThrowAxis.Right:
                seekerAnimator.SetBool("projectedH", false);
                break;

            case ThrowAxis.Left:
                seekerAnimator.SetBool("projectedH", false);
                break;

            case ThrowAxis.Backward:
                seekerAnimator.SetBool("projectedV", false);
                break;

            case ThrowAxis.Bottom:
                seekerAnimator.SetBool("projectedV", false);
                break;
        }*/
        if (obstacleMeet && obstaclePosition != Vector3.zero) //(AVEC impact)
        {
            //Particules d'impact, un effet de tourni + une chute de l'entit�
            if (OnEnemyImpact != null) OnEnemyImpact(obstaclePosition, true);
            falling = true;
        }
        else // (SANS impact)
        {
            //Effet de tourni pendant quelques secondes puis l'entit� peut de nouveau bouger.
            confusedTime = 5f;
        }

        ChangeSeekerAnimation(SeekerAnimState.Dizzy);
        if (!confusedParticles.isPlaying) confusedParticles.Play();

        throwingState = false;
    }

    public void HitByPlayerDash(Vector3 playerDashDirection)
    {
        if (!throwingState)
        {
            bouncyObject.enabled = false;

            //StartCoroutine(ThrownByDash(playerDashDirection));
            projectionCoroutine = StartCoroutine(ThrowToDirection(playerDashDirection, false, "projectedV", throwByDashTime, projectionByDashSpeed));
            if (OnEnemyImpact != null) OnEnemyImpact(transform.position, false);
            DamagesReceived(transform.position);
            if (playerTr.parent.TryGetComponent(out HeroMovements heroMovements))
            {
                heroMovements.StopDash();
                Debug.Log("STOP DASH");
            }

            MoveAtWill(false);
        }
    }

    IEnumerator ThrowToDirection(
        Vector3 direction, bool withThrowAxis, string throwAnimationName, float throwTime, float speed
        )
    {
        throwingState = true;
        falling = false;
        obstacleMeet = false;

        /* Animations & movements settings */
        if (grabbableBehavior) grabbableBehavior.enabled = false;
        if (grabbableBehavior) grabbableBehavior.grabVisibility.enabled = false;
        canFreelyMove = false;
        ChangeSeekerAnimation(SeekerAnimState.Thrown);
        //seekerAnimator.SetBool(throwAnimationName, true); //Faire attention au nom renseign�. (Vertical ou Horizontal)
        //Orienter les particules pour qu'elles laissent une tra�n�e en direction inverse de notre destination.
        if (!projectionParticles.isPlaying) projectionParticles.Play();
        projectionParticles.transform.LookAt(transform.position + direction);
        projectionParticles.transform.localRotation *= Quaternion.Euler(0, 180f, 0);

        bouncyObject.enabled = false;
        gameObject.layer = 2;
        heightBeforeFall = transform.position.y;

        /* Param�tres de la projection. */
        if (!withThrowAxis)
        {
            direction += Vector3.up; //Ajout d'un mouvement vers le haut pour r�aliser une courbe montante au d�but.
        }
        float startTime = Time.time;
        float fractionOfJourney;
        Vector3 nextPositionDir;

        /*Debug : */
        Vector3 startPosition = transform.position;

        do
        {
            fractionOfJourney = (Time.time - startTime) / throwTime;
            //Si fractionOfJourney atteint 1, cela veut dire que la vitesse de projection est minimale.
            if (fractionOfJourney > 1f)
            {
                fractionOfJourney = 1f;
                falling = true;
                throwingState = false;
                /*Debug : 
                Vector3 movedDist = transform.position - startPosition;
                Debug.Log("Distance parcourue : " + movedDist.magnitude);*/
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
                obstacleMeet = true;

                falling = true;

                throwingState = false;
                /*Debug : 
                Vector3 movedDist = transform.position - startPosition;
                Debug.Log("Distance parcourue : " + movedDist.magnitude);*/
            }
            yield return null;
        } while (throwingState);

        /* Une fois que l'entit� est retomb�e au sol (si morte de sa chute : faire autre chose.) */

        //Si on est assez proche du sol, on y colle l'entit� directement
        Ray ray = new Ray(transform.position, -transform.up);
        if (Physics.Raycast(ray, out RaycastHit hit, 1f, obstacleMask))
        {
            transform.position = hit.point + Vector3.up * heightOffset;
            obstacleMeet = true;
        }

        //seekerAnimator.SetBool(throwAnimationName, false);

        confusedTime = 5f;
        ChangeSeekerAnimation(SeekerAnimState.Dizzy);
        if (!confusedParticles.isPlaying) confusedParticles.Play();

        bouncyObject.enabled = true;
        bouncyObject.canBounce = true;
        gameObject.layer = LayerMask.NameToLayer("Enemy");
    }
    #endregion

    IEnumerator DistanceCheck(float checkFrequency)
    {
        do
        {
            float distance = (currentDestination - transform.position).sqrMagnitude;
            if (distance < 1f && !destinationReached) //should be : "dist < distRequired*distRequired", but it's one.
            {
                destinationReached = true;
                if(currentState == EnemyAIState.Searching)
                {
                    timeBfrNextSearch = Random.Range(2f, 4f);
                    ChangeSeekerAnimation(SeekerAnimState.Searching);
                } 
                else if(currentState == EnemyAIState.Wandering || currentState == EnemyAIState.StepBackToInitialPos)
                {
                    ChangeSeekerAnimation(SeekerAnimState.Searching);
                }
            }
            yield return new WaitForSeconds(checkFrequency);
        } while (canFreelyMove);
    }

    IEnumerator PlayerDistanceCheck(float checkFrequency)
    {
        do
        {
            distanceToPlayer = (playerTr.position - transform.position).magnitude;

            yield return new WaitForSeconds(checkFrequency);
        } while (isAlive);
    }

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
            confusedTime = 2f;
            ChangeSeekerAnimation(SeekerAnimState.Dizzy);
            canFreelyMove = false;
            if (!confusedParticles.isPlaying) confusedParticles.Play();
        }
    }

    private void Attack()
    {
        if (Physics.CheckSphere(transform.position + transform.forward, 1f, LayerMask.NameToLayer("Player")))
        {
            //Summon attack orb
            SeekerOrb seekerOrb = Instantiate(seekerOrbPrefab, orbInvocationPosition.position, Quaternion.identity).GetComponent<SeekerOrb>();
            seekerOrb.playerTr = playerTr;
            seekerOrb.SetOrbTarget(playerTr.position);
            
        }

        attacking = false;
        timeBfrNewAttack = 3.5f;
    }
    
    public void Death()
    {
        canFreelyMove = false;
        isAlive = false;
        seekerAnimator.ResetTrigger("Attack");
        seekerAnimator.SetTrigger("Die");
        Destroy(gameObject, 2f);
    }

    public void TargetDetected()
    {
        if (attackMode || confusedTime > 0f || !canFreelyMove || grabbed) return;

        attackMode = true;
        MoveAtWill(false);
        ChangeSeekerAnimation(SeekerAnimState.Thrown);
        expressionsManager.MakeExpression(MonsterExpressions.Surprise);
    }

    public void TargetLost()
    {
        attackMode = false;

        ChangeSeekerAnimation(SeekerAnimState.Idle);
        seekerAnimator.ResetTrigger("Attack");
        changeTime = 2f;

        expressionsManager.MakeExpression(MonsterExpressions.Question); //Fait appara�tre un point d'interrogation
    }

    IEnumerator RedFlashing(float flashDuration)
    {
        float flashTime = 0f;

        do
        {
            flashTime += Time.deltaTime;
            float lerp = flashHitCurve.Evaluate(flashTime / flashDuration);
            seekerRenderer.material.SetFloat("LerpToFlash", lerp);

            yield return null;
        } while (flashTime < flashDuration);

        seekerRenderer.material.SetFloat("LerpToFlash", 0f);
    }
    #endregion
}
