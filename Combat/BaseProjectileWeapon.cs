using UnityEngine;
using DG.Tweening;
using Unity.Netcode;
using UnityStandardAssets.CrossPlatformInput;

/// <summary>
/// An override of BaseWeapon implementing projectile functionality
/// </summary>
public class BaseProjectileWeapon : BaseWeapon
{
    /*[Header("BaseProjectileWeapon")]
    [Header("Components")]
    /// <summary> The player's camera</summary>
    [SerializeField] private PlayerCamera playerCamera = default;

    /// <summary> The FOV upon holding the attack button, ie "aiming"</summary>
    [Header("Variables")]

    /// <summary> Animation pause delayer</summary>
    [SerializeField] private float pauseAttackAnimationDelay = 0;

    /// <summary> The actual projectile this weapon fires</summary>
    [Header("Variables - Projectile")]
    [SerializeField] private BaseProjectile baseProjectile = default;

    /// <summary> The speed of the spawned projectile</summary>
    [SerializeField] private float projectileSpeed = 10.0f;

    /// <summary> For how long this projectile should stay alive </summary>
    [SerializeField] private float projectileLifetime= 10.0f;

    //make a "hardcoded" LookAtTarget position when aiming the bow
    //[SerializeField] private Transform lookAtTargetWhenAimed = default;

    /// <summary> The initial player camera FOV value</summary>
    protected float initialPlayerCameraFOV;

    /// <summary> Is the player "aiming"?</summary>
    protected bool isAiming;

    /// <summary> This is a tween to delay when to pause the animation after attack</summary>
    private Tween drawBowDelayTween = default;

    // Start is called before the first frame update
    void Start()
    {
        if(IsOwner)
        { 
            initialPlayerCameraFOV = playerCamera.Camera.fieldOfView; 
        }
    }

    protected override void Awake()
    {
        base.Awake();
    }

    /// <summary> Override Update() from BaseWeapon</summary>
    public override void CheckAttack()
    {
        // If we are pressing the attack button, can attack and NOT aiming
        if (CrossPlatformInputManager.GetButtonDown(attackButton) && CanAttack() && !isAiming)
        {
            // Draw the bow
            TryDrawBowServerRpc();
        }

        // If we've released the attack button and IS aiming
        if (CrossPlatformInputManager.GetButtonUp(attackButton) && isAiming)
        {
            // Shoot the bow calculated from player pos & direction
            Vector3 playerPos = new Vector3(transform.root.gameObject.transform.position.x, 0.4f, transform.root.gameObject.transform.position.z);
            Vector3 playerDirection = transform.root.gameObject.transform.forward;

            Vector3 spawnPos = playerPos + transform.root.gameObject.transform.forward * 1f;
            TryAttackProjectileWeaponServerRpc(spawnPos, playerDirection);
        }
    }

    /// <summary> Server RPC to draw bow, this needs to be networked because aiming should be networked (animation etc)</summary>
    [ServerRpc]
    private void TryDrawBowServerRpc()
    {
        DrawBowClientRpc();    
    }

    /// <summary> Draws the bow</summary>
    [ClientRpc]
    private void DrawBowClientRpc()
    {
        if (IsOwner)
        {
            DoZoom(true);
        }

        // Start the player attack animation, which is overridden by this weapons override controller
        playerAnimator.AnimationTrigger((byte)weaponAnimationTriggerType);

        if (drawBowDelayTween != null)
        {
            drawBowDelayTween.Kill();
        }

        // With throwable weapons, you sometimes wanna pause the attack animation incase of a "primed" attack (ie player holds the "load up" animation)
        drawBowDelayTween = DOVirtual.DelayedCall(pauseAttackAnimationDelay / this.weaponAttackSpeed, () =>
        {
            // Pause the animation by setting speed to 0. This is to "draw the bow
            playerAnimator.SetPlayerAttackSpeed(0);
        });
    }


    /// <summary> ServerRpc to spawn projectile</summary>
    /// <param name="weaponDirection"></param>
    [ServerRpc]
    protected void TryAttackProjectileWeaponServerRpc(Vector3 shotSpawnPosition, Vector3 weaponDirection)
    {
        OnPrimaryAttackProjectileWeaponClientRpc(shotSpawnPosition, weaponDirection);

        // Server needs the arrow spawned too, to simulate collisions, it is not a client, so it wont receive the ClientRpc
        if(!IsHost)
        {
            var projectile = Instantiate(baseProjectile, shotSpawnPosition, Quaternion.identity);
            projectile.InitializeProjectile(weaponDamage, projectileSpeed, weaponDirection, this.transform.root.gameObject, OwnerClientId);
        }
    }

    /// <summary> The primary attack for this projectile weapon </summary>
    /// <param name="playerCameraForward"></param>
    [ClientRpc]
    protected void OnPrimaryAttackProjectileWeaponClientRpc(Vector3 shotSpawnPosition, Vector3 weaponDirection)
    {
        //Don't call base!
        //base.OnPrimaryAttack();

        if (IsOwner)
        {
            DoZoom(false);          
        }

        if (drawBowDelayTween != null)
        {
            drawBowDelayTween.Kill();
        }

        // Restore the weaponAttack speed to continue the animation
        playerAnimator.SetPlayerAttackSpeed(weaponAttackSpeed);

        var projectile = Instantiate(baseProjectile, shotSpawnPosition, Quaternion.identity);
        projectile.InitializeProjectile(weaponDamage, projectileSpeed, weaponDirection, this.transform.root.gameObject, OwnerClientId);

        #if UNITY_EDITOR
        Debug.DrawRay(new Vector3(shotSpawnPosition.x, shotSpawnPosition.y + 1f, shotSpawnPosition.z) + (transform.forward), weaponDirection * 50, Color.red, 10.0f);
        #endif

        //destroy after 10 secs by default
        Destroy(projectile.gameObject, projectileLifetime);
    }

    /// <summary> Zoom in/out, when player is aiming</summary>
    private void DoZoom(bool toggleZoom)
    {
        if (IsOwner)
        {
            if(toggleZoom == false)
            {
                // Zoom out, player was aiming
                isAiming = false;
                transform.root.GetComponent<PlayerController>().SetAimState(false);
                playerCamera.DoZoom(false);
            }
            else
            {
                // Zoom in, player is aiming
                isAiming = true;
                transform.root.GetComponent<PlayerController>().SetAimState(true);
                playerCamera.DoZoom(true);
            }
        }
    }

    /// <summary> When bow gets unequipped, this happens. </summary>
    public override void OnWeaponUnequipped(CombatController weaponController)
    {
        base.OnWeaponUnequipped(weaponController);
        if (IsOwner && isAiming)
        {
            DoZoom(false);
        }
    }*/

}
