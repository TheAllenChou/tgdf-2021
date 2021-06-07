
/******************************************************************************/
/*
  Gamedev Tutorial
  Author - Ming-Lun "Allen" Chou
           http://AllenChou.net
*/
/******************************************************************************/

using UnityEngine;
using UnityEngine.AI;

public class Main : MonoBehaviour
{
  public enum ModeEnum
  {
    SingleThreaded, 
    MultiThreadedGatherSameFrame, 
    MultiThreadedGatherNextFrame, 
    MultiThreadedTimeSliced, 
  }

  [Range(10, 100)] public int GridDimension = 10;
  public ModeEnum Mode = ModeEnum.SingleThreaded;
  private ModeEnum s_prevMode = (ModeEnum) (-1);

  [ConditionalField("Mode", ModeEnum.MultiThreadedTimeSliced, Min = 0, Max = 100)]
  public int TimeSlicePercentage = 10;
  
  public bool DrawRays = true;
  public bool DrawExposure = true;

  private GameObject m_character;
  private GameObject m_sentinel;
  private GameObject m_ground;
  private Vector3 m_destination;

  private void OnEnable()
  {
    GameManager.Init();

    m_character = GameObject.Find("Character");
    m_sentinel = GameObject.Find("Sentinel");
    m_destination = m_character.transform.position;
  }

  private void OnDisable()
  {
    GameManager.Dispose();
  }

  private void Update()
  {
    GameManager.SetGridDimension(GridDimension);

    if (s_prevMode != Mode)
    {
      GameManager.Reboot();
      s_prevMode = Mode;
    }

    Vector3 eyePos = m_sentinel.transform.position;

    switch (Mode)
    {
      case ModeEnum.SingleThreaded:
        GameManager.UpdateExposureSingleThreaded(eyePos, DrawRays);
        break;

      case ModeEnum.MultiThreadedGatherSameFrame:
        GameManager.UpdateExposreMultiThreadedGatherSameFrame(eyePos, DrawRays);
        break;

      case ModeEnum.MultiThreadedGatherNextFrame:
        GameManager.UpdateExposreMultiThreadedGatherNextFrame(eyePos, DrawRays);
        break;

      case ModeEnum.MultiThreadedTimeSliced:
        int numRaysPerTimeSlice = Mathf.Max(1, Mathf.CeilToInt(GameManager.NumCells * (TimeSlicePercentage / 100.0f)));
        GameManager.UpdateExposreMultiThreadedTimeSliced(eyePos, numRaysPerTimeSlice, DrawRays);
        break;
    }

    if (GameManager.IsExposed(m_destination))
    {
      Vector3 newPos;
      if (GameManager.GetNearestBlockedPosition(m_destination, out newPos))
      {
        m_destination = newPos;
        m_character.GetComponent<NavMeshAgent>().SetDestination(m_destination);
      }
    }

    if (DrawExposure)
    {
      GameManager.DebugDrawExposureMap();
    }
  }
}

