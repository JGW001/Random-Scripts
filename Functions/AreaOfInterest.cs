using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;
using Unity.Netcode;

public class AreaOfInterest : NetworkBehaviour
{
    [SerializeField] public UnityEvent NobodyObserving;
    [SerializeField] public UnityEvent PeopleObserving;
    [HideInInspector] public NetworkObject networkObject = default;
    private bool EventExecuted = true;
    /// <summary> Current Observers of this AOI Object.</summary>
    [SerializeField] private List<GameObject> Observers = new List<GameObject>();

    private void Awake()
    {
        if (networkObject == null)
        {
            networkObject = GetComponent<NetworkObject>();
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        networkObject.CheckObjectVisibility = ((clientId) => { return false; });

        if(IsServer)
        {
            InvokeRepeating("CleanUpObservers", 60f, 60f);
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
    }

    public void AddToObserverCount(GameObject playerObject)
    {
        Observers.Add(playerObject);

        if (EventExecuted == true && Observers.Count > 0)
        {
            PeopleObserving.Invoke();
            EventExecuted = false;
        }
    }

    public void RemoveFromObserverCount(GameObject playerObject)
    {
        for (int i = 0; i < Observers.Count; i++)
        {
            if(Observers[i] == playerObject)
                Observers.RemoveAt(i);
        }

        if (Observers.Count == 0)
        {
            NobodyObserving.Invoke();
            EventExecuted = true;
        }
    }

    private void CleanUpObservers()
    {
        for (int i = 0; i < Observers.Count; i++)
        {
            if (Observers[i] == null)
            {
                Observers.RemoveAt(i);
            }
        }

        if(Observers.Count == 0 && !IsHost) // We don't want to Invoke this event on host games as it's testing
        {
            NobodyObserving.Invoke();
            EventExecuted = true;
        }
    }
}
