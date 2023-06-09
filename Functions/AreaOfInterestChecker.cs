using UnityEngine;
using Unity.Netcode;

// Denne fil bliver kørt på selve serveren, og scanner alle klienters nuværende position i spil verdenen, for at "gemme" eller "vise" andre spillere, som er indenfor rækkevide.
// Dette bliver gjort for at spare på data, så spillere som er langt væk fra hinanden, ikke modtager opdateringer fra hinanden over netværket.

public class AreaOfInterestChecker : NetworkBehaviour
{
    /// <summary> The layer mask for which layers should be checked for AreaOfInterest script</summary>
    [SerializeField] protected LayerMask aoiRegistrationLayerMask = default;
    /// <summary> Every X amount of seconds, the server will run a AOI check on the player</summary>
    private float areaOfInterestTimer = 2.5f;
    /// <summary> Stored float timer to check if it has hit areaOfInterestTimer</summary>
    private float savedAreaOfInterestTimer = 0f;
    /// <summary> The distance the server should check for AOI objects</summary>
    [SerializeField] private float syncDistance = 25f;

    private void FixedUpdate()
    {
        if (!IsServer) return;
        AreaOfInterestCheck();
    }

    private void AreaOfInterestCheck()
    {
        savedAreaOfInterestTimer += Time.deltaTime; // Increase timer

        if (savedAreaOfInterestTimer > (areaOfInterestTimer / 2))
        {
            // Scan through all players, and run a Area of Interest check on them
            foreach (ulong networkPlayerId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (networkPlayerId == OwnerClientId) continue;                                             // Skip for the server/host
                if (NetworkManager.SpawnManager.GetPlayerNetworkObject(networkPlayerId) == null) continue; 

                GameObject tmpPlayerObject = NetworkManager.SpawnManager.GetPlayerNetworkObject(networkPlayerId).gameObject;    // Get the current player in loop gameObject
                var tmpNetworkedPlayer = tmpPlayerObject.GetComponent<NetworkedPlayer>();                                       // Grab the NetworkedPlayer component using the gameObject from above

                if (savedAreaOfInterestTimer > (areaOfInterestTimer / 2))
                {
                    RaycastHit[] checker;
                    checker = Physics.SphereCastAll(tmpPlayerObject.transform.position, syncDistance, tmpPlayerObject.transform.forward, 0f, aoiRegistrationLayerMask); // Check if somebody else are in range of player.
                    for (int i = 0; i < checker.Length; i++)
                    {
                        if (checker[i].collider.gameObject.TryGetComponent(out AreaOfInterest aoiObject))   // Checking detected object(s) if they have the area of interest script on them
                        {
                            if (!tmpNetworkedPlayer.shownObjects.Contains(checker[i].collider.gameObject))  // If detected object does not have the current player as "shown", show them.
                            {
                                tmpNetworkedPlayer.shownObjects.Add(checker[i].collider.gameObject);        // Add it to the list
                                aoiObject.AddToObserverCount(gameObject);                                   

                                if (!aoiObject.networkObject.IsNetworkVisibleTo(networkPlayerId)) // If it's not currently visible, show it.
                                    aoiObject.networkObject.NetworkShow(networkPlayerId);                   // Show the object over the network for the current player in loop.
                            }
                        }
                    }
                }
            }

        }
        
        // This is the same as above, but this part hides the player, incase the range is over a certain distance.

        if (savedAreaOfInterestTimer > areaOfInterestTimer)
        {
            // Scan through all players, and run a Area of Interest check on them
            foreach (ulong networkPlayerId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (networkPlayerId == OwnerClientId) continue;                                             // Skip for the server/host
                if (NetworkManager.SpawnManager.GetPlayerNetworkObject(networkPlayerId) == null) continue; 

                GameObject tmpPlayerObject = NetworkManager.SpawnManager.GetPlayerNetworkObject(networkPlayerId).gameObject;
                var tmpNetworkedPlayer = tmpPlayerObject.GetComponent<NetworkedPlayer>();

                for (int i = tmpNetworkedPlayer.shownObjects.Count - 1; i >= 0; i--)
                {
                    if (tmpNetworkedPlayer.shownObjects[i] == null)
                    {
                        tmpNetworkedPlayer.shownObjects.RemoveAt(i);
                        continue;
                    }

                    if (Vector3.Distance(tmpPlayerObject.transform.position, tmpNetworkedPlayer.shownObjects[i].transform.position) > (syncDistance + 5f))
                    {
                        if (tmpNetworkedPlayer.shownObjects[i].TryGetComponent(out AreaOfInterest aoiObject))
                        {
                            tmpNetworkedPlayer.shownObjects.RemoveAt(i);
                            aoiObject.RemoveFromObserverCount(gameObject);
                            aoiObject.networkObject.NetworkHide(networkPlayerId);
                        }
                    }
                }
            }

            savedAreaOfInterestTimer = 0f;
        }
    }
}
