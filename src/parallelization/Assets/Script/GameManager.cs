/******************************************************************************/
/*
  Gamedev Tutorial
  Author - Ming-Lun "Allen" Chou
           http://AllenChou.net
*/
/******************************************************************************/

using System;

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Profiling;

using CjLib;

public class GameManager : MonoBehaviour
{
  // data
  //---------------------------------------------------------------------------

  public static int m_gridDimension = 10;
  public static int NumCells => m_gridDimension * m_gridDimension;
  public static readonly float CellSize = 1.0f;

  private static NativeArray<Vector3> s_grid;
  private static NativeArray<float> s_pathLengthMap;
  private static NativeArray<bool> s_clearanceMap;
  private static NativeArray<bool> s_exposureMap;
  private static NativeArray<bool> s_exposureMapBackBuffer;

  public static void SetGridDimension(int dimension)
  {
    m_gridDimension = dimension;
    ValidateData();
  }

  private static void ValidateData()
  {
    if (s_grid.IsCreated)
    {
      if (s_grid.Length == NumCells)
        return;

      Dispose();
    }

    s_grid = new NativeArray<Vector3>(NumCells, Allocator.Persistent);
    s_pathLengthMap = new NativeArray<float>(NumCells, Allocator.Persistent);
    s_clearanceMap = new NativeArray<bool>(NumCells, Allocator.Persistent);
    s_exposureMap = new NativeArray<bool>(NumCells, Allocator.Persistent);
    s_exposureMapBackBuffer = new NativeArray<bool>(NumCells, Allocator.Persistent);

    s_timeSliceBaseIndex = 0;

    for (int row = 0; row < m_gridDimension; ++row)
    {
      for (int col = 0; col < m_gridDimension; ++col)
      {
        int iCell = GetCellIndex(row, col);
        Vector3 center = GetCellCenter(row, col);
        bool navigable = !Physics.Raycast(center + 10.0f * Vector3.up, Vector3.down);

        s_grid[iCell] = center;
        s_pathLengthMap[iCell] = float.MaxValue;
        s_clearanceMap[iCell] = navigable;
        s_exposureMap[iCell] = false;
      }
    }
  }

  public static void Init()
  {
    ValidateData();
  }

  public static void Dispose()
  {
    s_hGatherJob.Complete();

    if (s_grid.IsCreated)
      s_grid.Dispose();

    if (s_pathLengthMap.IsCreated)
      s_pathLengthMap.Dispose();

    if (s_clearanceMap.IsCreated)
      s_clearanceMap.Dispose();

    if (s_exposureMap.IsCreated)
      s_exposureMap.Dispose();

    if (s_exposureMapBackBuffer.IsCreated)
      s_exposureMapBackBuffer.Dispose();
  }

  public static void Reboot()
  {
    s_hGatherJob.Complete();

    if (s_commands.IsCreated)
      s_commands.Dispose();

    if (s_results.IsCreated)
      s_results.Dispose();

    s_timeSliceBaseIndex = 0;
  }

  private static void SwapExposureBackBuffer()
  {
    var temp = s_exposureMap;
    s_exposureMap = s_exposureMapBackBuffer;
    s_exposureMapBackBuffer = temp;
  }

  //---------------------------------------------------------------------------
  // end: data


  // queries
  //---------------------------------------------------------------------------

  public static int NumJobs => Math.Max(2, Environment.ProcessorCount) - 1;
  public static int GetJobBatchSize(int totalBatchSize)
  {
    return (int) Mathf.Ceil(totalBatchSize / NumJobs);
  }

  public static int GetCellIndex(int row, int col)
  {
    return row * m_gridDimension + col;
  }

  public static Vector3 GetCellCenter(int row, int col)
  {
    return 
      new Vector3
      (
        (-m_gridDimension / 2 + col + 0.5f) * CellSize, 
        0.0f, 
        (-m_gridDimension / 2 + row + 0.5f) * CellSize
      );
  }

  public static Vector3 GetNearestCellCenter(Vector3 pos)
  {
    int row, col;
    PositionToCell(pos, out row, out col);
    return GetCellCenter(row, col);
  }

  public static void PositionToCell(Vector3 pos, out int row, out int col)
  {
    row = (int) Mathf.Floor(pos.z / CellSize) + m_gridDimension / 2;
    col = (int) Mathf.Floor(pos.x / CellSize) + m_gridDimension / 2;
  }

  public static void PositionToCell(Vector3 pos, out int iCell)
  {
    int row = (int) Mathf.Floor(pos.z / CellSize) + m_gridDimension / 2;
    int col = (int) Mathf.Floor(pos.x / CellSize) + m_gridDimension / 2;
    iCell = GetCellIndex(row, col);
  }

  public static bool IsExposed(Vector3 pos)
  {
    int iCell;
    PositionToCell(pos, out iCell);

    if (iCell < 0 || iCell > s_exposureMap.Length)
      return false;

    return s_exposureMap[iCell];
  }

  //---------------------------------------------------------------------------
  // end: queries


  // single-threaded
  //---------------------------------------------------------------------------

  private static Vector3[] s_aRayDir;
  private static float[] s_aRayLen;

  public static void UpdateExposureSingleThreaded(Vector3 eyePos, bool drawRays)
  {
    if (s_aRayDir == null || s_aRayDir.Length != NumCells)
    {
      s_aRayDir = new Vector3[NumCells];
      s_aRayLen = new float[NumCells];
    }

    for (int iCell = 0; iCell < NumCells; ++iCell)
    {
      if (!s_clearanceMap[iCell])
        continue;

      Vector3 cellCenter = s_grid[iCell];
      Vector3 vec = cellCenter - eyePos;
      s_aRayDir[iCell] = vec.normalized;
      s_aRayLen[iCell] = vec.magnitude;
    }

    Profiler.BeginSample("UpdateExposureSingleThreaded");
    {
      for (int iCell = 0; iCell < NumCells; ++iCell)
      {
        if (!s_clearanceMap[iCell])
          continue;

        bool exposed = !Physics.Raycast(eyePos, s_aRayDir[iCell], s_aRayLen[iCell]);
        s_exposureMap[iCell] = exposed;
      }
    }
    Profiler.EndSample();

    if (drawRays)
    {
      for (int iCell = 0; iCell < NumCells; ++iCell)
      {
        if (!s_clearanceMap[iCell])
          continue;

        Color color = s_exposureMap[iCell] ? Color.magenta : Color.cyan;
        DebugUtil.DrawLine(eyePos, s_grid[iCell], color);
      }
    }
  }

  //---------------------------------------------------------------------------
  // end: single-threaded


  // multi-threaded
  //---------------------------------------------------------------------------

  private static JobHandle s_hGatherJob;
  private static NativeArray<RaycastCommand> s_commands;
  private static NativeArray<RaycastHit> s_results;
  private static Vector3 s_timeSliceEyePos;
  private static int s_timeSliceBaseIndex;

  [BurstCompile]
  private struct RaycastSetupJob : IJobParallelFor
  {
    public Vector3 EyePos;
    [ReadOnly] public NativeArray<Vector3> Grid;
    [ReadOnly] public NativeArray<bool> ClearanceMap;
    [WriteOnly] public NativeArray<RaycastCommand> Commands;

    public int TimeSliceBaseIndex;

    public void Execute(int localIndex)
    {
      int globalIndex = localIndex + TimeSliceBaseIndex;

      if (!ClearanceMap[globalIndex])
        return;

      Vector3 cellCenter = Grid[globalIndex];
      Vector3 vec = cellCenter - EyePos;
      Commands[localIndex] = new RaycastCommand(EyePos, vec.normalized, vec.magnitude);
    }
  }

  [BurstCompile]
  private struct RaycastGatherJob : IJobParallelFor
  {
    [ReadOnly] public NativeArray<bool> ClearanceMap;
    [ReadOnly] public NativeArray<RaycastHit> Results;
    [NativeDisableParallelForRestriction] public NativeArray<bool> ExposureMap;

    public int TimeSliceBaseIndex;

    public void Execute(int localIndex)
    {
      int globalIndex = localIndex + TimeSliceBaseIndex;

      if (!ClearanceMap[globalIndex])
        return;

      bool exposed = (Results[localIndex].distance <= 0.0f);
      ExposureMap[globalIndex] = exposed;
    }
  }

  public static void UpdateExposreMultiThreadedGatherSameFrame(Vector3 eyePos, bool drawRays)
  {
    Profiler.BeginSample("UpdateExposreMultiThreadedGatherSameFrame");
    {
      var commands = new NativeArray<RaycastCommand>(NumCells, Allocator.TempJob);
      var results = new NativeArray<RaycastHit>(NumCells, Allocator.TempJob);
      int jobBatchSize = GetJobBatchSize(NumCells);

      var hCommandJob = 
        new RaycastSetupJob
        {
          EyePos = eyePos, 
          Grid = s_grid, 
          ClearanceMap = s_clearanceMap, 
          Commands = commands, 
          TimeSliceBaseIndex = 0, 
        }.Schedule(NumCells, jobBatchSize);

      var hRaycastJob = RaycastCommand.ScheduleBatch(commands, results, jobBatchSize, hCommandJob);

      var hGatherJob = 
        new RaycastGatherJob
        {
          ClearanceMap = s_clearanceMap, 
          Results = results, 
          ExposureMap = s_exposureMap, 
          TimeSliceBaseIndex = 0, 
        }.Schedule(NumCells, jobBatchSize, hRaycastJob);

      JobHandle.ScheduleBatchedJobs();

      hGatherJob.Complete();

      commands.Dispose();
      results.Dispose();
    }
    Profiler.EndSample();

    if (drawRays)
    {
      for (int iCell = 0; iCell < NumCells; ++iCell)
      {
        if (!s_clearanceMap[iCell])
          continue;

        Color color = s_exposureMap[iCell] ? Color.magenta : Color.cyan;
        DebugUtil.DrawLine(eyePos, s_grid[iCell], color);
      }
    }
  }

  public static void UpdateExposreMultiThreadedGatherNextFrame(Vector3 eyePos, bool drawRays)
  {
    Profiler.BeginSample("UpdateExposreMultiThreadedGatherNextFrame");
    {
      s_hGatherJob.Complete();

      SwapExposureBackBuffer();

      if (s_commands.IsCreated)
        s_commands.Dispose();

      if (s_results.IsCreated)
        s_results.Dispose();

      if (drawRays)
      {
        for (int iCell = 0; iCell < NumCells; ++iCell)
        {
          if (!s_clearanceMap[iCell])
            continue;

          Color color = s_exposureMap[iCell] ? Color.magenta : Color.cyan;
          DebugUtil.DrawLine(eyePos, s_grid[iCell], color);
        }
      }

      s_commands = new NativeArray<RaycastCommand>(NumCells, Allocator.TempJob);
      s_results = new NativeArray<RaycastHit>(NumCells, Allocator.TempJob);
      int jobBatchSize = GetJobBatchSize(NumCells);

      var hCommandJob = 
        new RaycastSetupJob
        {
          EyePos = eyePos, 
          Grid = s_grid, 
          ClearanceMap = s_clearanceMap, 
          Commands = s_commands,
          TimeSliceBaseIndex = 0,
        }.Schedule(NumCells, jobBatchSize);

      var hRaycastJob = RaycastCommand.ScheduleBatch(s_commands, s_results, jobBatchSize, hCommandJob);

      s_hGatherJob = 
        new RaycastGatherJob
        {
          ClearanceMap = s_clearanceMap, 
          Results = s_results, 
          ExposureMap = s_exposureMapBackBuffer,
          TimeSliceBaseIndex = 0,
        }.Schedule(NumCells, jobBatchSize, hRaycastJob);

      JobHandle.ScheduleBatchedJobs();
    }
    Profiler.EndSample();
  }

  public static void UpdateExposreMultiThreadedTimeSliced(Vector3 eyePos, int numRaysPerTimeSlice, bool drawRays)
  {
    Profiler.BeginSample("UpdateExposreMultiThreadedTimeSliced");
    {
      s_hGatherJob.Complete();

      int numExcessRays = Mathf.Max(0, s_timeSliceBaseIndex + numRaysPerTimeSlice - NumCells);
      numRaysPerTimeSlice -= numExcessRays;
      int timeSliceEndIndex = s_timeSliceBaseIndex + numRaysPerTimeSlice;

      // batch ended?
      if (s_timeSliceBaseIndex < 0)
      {
        SwapExposureBackBuffer();
        s_timeSliceBaseIndex = 0;
      }

      // start of new batch?
      if (s_timeSliceBaseIndex == 0)
      {
        s_timeSliceEyePos = eyePos;
      }

      if (s_commands.IsCreated)
        s_commands.Dispose();

      if (s_results.IsCreated)
        s_results.Dispose();

      if (drawRays)
      {
        for (int iCell = s_timeSliceBaseIndex; iCell < timeSliceEndIndex; ++iCell)
        {
          if (!s_clearanceMap[iCell])
            continue;

          Color color = s_exposureMap[iCell] ? Color.magenta : Color.cyan;
          DebugUtil.DrawLine(eyePos, s_grid[iCell], color);
        }
      }

      s_commands = new NativeArray<RaycastCommand>(numRaysPerTimeSlice, Allocator.TempJob);
      s_results = new NativeArray<RaycastHit>(numRaysPerTimeSlice, Allocator.TempJob);
      int jobBatchSize = GetJobBatchSize(numRaysPerTimeSlice);

      var hCommandJob = 
        new RaycastSetupJob
        {
          EyePos = s_timeSliceEyePos, 
          Grid = s_grid, 
          ClearanceMap = s_clearanceMap, 
          Commands = s_commands, 
          TimeSliceBaseIndex = s_timeSliceBaseIndex, 
        }.Schedule(numRaysPerTimeSlice, jobBatchSize);

      var hRaycastJob = RaycastCommand.ScheduleBatch(s_commands, s_results, jobBatchSize, hCommandJob);

      s_hGatherJob = 
        new RaycastGatherJob
        {
          ClearanceMap = s_clearanceMap, 
          Results = s_results, 
          ExposureMap = s_exposureMapBackBuffer,
          TimeSliceBaseIndex = s_timeSliceBaseIndex,
        }.Schedule(numRaysPerTimeSlice, jobBatchSize, hRaycastJob);

      s_timeSliceBaseIndex += numRaysPerTimeSlice;

      // end of batch?
      if (s_timeSliceBaseIndex >= NumCells)
      {
        s_timeSliceBaseIndex = -1;
      }

      JobHandle.ScheduleBatchedJobs();
    }
    Profiler.EndSample();
  }

  //---------------------------------------------------------------------------
  // end: multi-threaded


  // processing
  //---------------------------------------------------------------------------

  private static void UpdatePathLengths(Vector3 origin)
  {
    var path = new NavMeshPath();
    for (int iCell = 0; iCell < NumCells; ++iCell)
    {
      if (!s_clearanceMap[iCell])
        continue;

      Vector3 cellCenter = s_grid[iCell];
      float pathLen = float.MaxValue;
      if (NavMesh.CalculatePath(origin, cellCenter, NavMesh.AllAreas, path))
      {
        pathLen = 0.0f;
        for (int iCorner = 1; iCorner < path.corners.Length; ++iCorner)
          pathLen += Vector3.Distance(path.corners[iCorner - 1], path.corners[iCorner]);
      }
      s_pathLengthMap[iCell] = pathLen;
    }
  }

  public static bool GetNearestBlockedPosition(Vector3 pos, out Vector3 blockedPos)
  {
    UpdatePathLengths(pos);

    float bestPathLen = float.MaxValue;
    int iBestCell = -1;
    for (int iCell = 0; iCell < NumCells; ++iCell)
    {
      if (!s_clearanceMap[iCell])
        continue;

      if (s_exposureMap[iCell])
        continue;

      float pathLen = s_pathLengthMap[iCell];
      if (pathLen >= bestPathLen)
        continue;

      bestPathLen = pathLen;
      iBestCell = iCell;
    }

    if (iBestCell < 0)
    {
      blockedPos = pos;
      return false;
    }

    blockedPos = s_grid[iBestCell];
    return true;
  }

  //---------------------------------------------------------------------------
  // end: processing


  // debug
  //---------------------------------------------------------------------------

  public static void DebugDrawExposureMap()
  {
    for (int iCell = 0; iCell < NumCells; ++iCell)
    {
      if (!s_clearanceMap[iCell])
        continue;

      Vector3 cellCenter = s_grid[iCell];
      bool exposed = s_exposureMap[iCell];

      Color fillColor = exposed ? new Color(1.0f, 0.0f, 0.0f, 0.3f) : new Color(0.0f, 1.0f, 0.0f, 0.3f);
      Color lineColor = new Color(1.0f, 1.0f, 1.0f, 0.8f);
      Vector3 center = cellCenter + 0.01f * Vector3.up;
      Vector3 size = new Vector3(0.9f * CellSize, 0.001f, 0.9f * CellSize);
      DebugUtil.DrawBox(center, Quaternion.identity, size, fillColor, true, DebugUtil.Style.FlatShaded);
      //DebugUtil.DrawBox(center, Quaternion.identity, size, lineColor, true, DebugUtil.Style.Wireframe);
    }
  }

  //---------------------------------------------------------------------------
  // end: debug
}

