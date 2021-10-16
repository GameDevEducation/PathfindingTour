using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BasicNavigator : MonoBehaviour
{
    [SerializeField] string PathdataUID;
    [SerializeField] Transform DestinationMarker;
    [SerializeField] float DestinationMovedThreshold = 1f;
    [SerializeField] float NodeReachedThreshold = 0.25f;

    [SerializeField] float MoveSpeed = 10f;
    [SerializeField] float PathdataHeightOffset = 1f;
    [SerializeField] int LookAheadRange = 5;

    Vector3 PreviousDestination;

    List<PathdataNode> Path = null;
    int TargetNode = -1;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        // if the destination has moved then repath
        if (Vector3.Distance(PreviousDestination, DestinationMarker.position) >= DestinationMovedThreshold)
        {
            PreviousDestination = DestinationMarker.position;

            PathfindingManager.Instance.RequestPath_Synchronous(PathdataUID, transform.position, DestinationMarker.position,
                                                                delegate (PathdataNode current, PathdataNode destination)
                                                                {
                                                                    return Vector3.Distance(current.WorldPos, destination.WorldPos);
                                                                },
                                                                out Path);

            // did we find a path
            if (Path != null && Path.Count > 0)
                TargetNode = 0;
        }

        if (Path != null && Path.Count > 0)
        {
            // move towards the target?
            Vector3 targetPosition = Path[TargetNode].WorldPos + Vector3.up * PathdataHeightOffset;
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, MoveSpeed * Time.deltaTime);

            Debug.DrawLine(transform.position, targetPosition, Color.green);

            Vector3 positionDelta = targetPosition - transform.position;
            positionDelta.y = 0;
            if (positionDelta.magnitude <= NodeReachedThreshold)
            {
                ++TargetNode;

                // reached the last node?
                if (TargetNode >= Path.Count)
                {
                    Path = null;
                    Debug.Log("Reached destination");
                }
                else
                {
                    for (int nodeOffset = LookAheadRange; nodeOffset > 0; --nodeOffset)
                    {
                        int candidateNode = TargetNode + nodeOffset;

                        // node doesn't exist
                        if (candidateNode >= Path.Count)
                            continue;

                        // can we take a shortcut
                        if (PathfindingManager.Instance.CanWalkBetween(PathdataUID, Path[TargetNode - 1], Path[candidateNode]))
                        {
                            TargetNode = candidateNode;
                            break;
                        }
                    }
                }
            }
        }
    }
}
