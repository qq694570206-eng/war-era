// ================================================================
// TouchInputSystem.cs — 移动端触屏输入系统
// 点选/框选/移动/攻击/建造/编队/镜头控制
// ================================================================
using System.Collections.Generic;
using UnityEngine;

public class TouchInputSystem : MonoBehaviour
{
    public static TouchInputSystem Instance { get; private set; }

    [Header("Camera")]
    public Camera mainCamera;
    public float cameraMoveSpeed = 15f;
    public float minZoom = 5f;
    public float maxZoom = 20f;
    public float zoomSpeed = 0.5f;
    public Vector2 worldBounds = new Vector2(2400, 1600);
    public float edgeMargin = 100f;

    [Header("Selection")]
    public List<UnitEntity> selectedUnits = new List<UnitEntity>();
    public float longPressTime = 0.18f;
    public float tapMoveThreshold = 10f;

    [Header("Squads")]
    public List<UnitEntity>[] squads = new List<UnitEntity>[5];

    private Vector2 touchStartPos;
    private float touchStartTime;
    private bool isPanning = false;
    private bool isBoxSelecting = false;
    private Vector3 boxStartWorld;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (mainCamera == null) mainCamera = Camera.main;
        for (int i = 0; i < 5; i++) squads[i] = new List<UnitEntity>();
    }

    void Update()
    {
        HandleInput();
        HandleCameraEdges();
    }

    void HandleInput()
    {
        // 单指操作
        if (Input.touchCount == 1)
        {
            Touch touch = Input.GetTouch(0);
            Vector3 worldPos = mainCamera.ScreenToWorldPoint(touch.position);
            worldPos.z = 0;

            switch (touch.phase)
            {
                case TouchPhase.Began:
                    touchStartPos = touch.position;
                    touchStartTime = Time.time;
                    isPanning = false;
                    isBoxSelecting = false;

                    // 如果在建造模式
                    if (BuildSystem.Instance?.isPlacing == true)
                    {
                        BuildSystem.Instance.PlaceBuilding(worldPos);
                        return;
                    }

                    // 检查是否点击了单位
                    UnitEntity clicked = PickUnit(worldPos);
                    if (clicked != null)
                    {
                        if (!clicked.isEnemy)
                        {
                            if (!Input.GetKey(KeyCode.LeftShift)) ClearSelection();
                            if (!selectedUnits.Contains(clicked)) selectedUnits.Add(clicked);
                            UIManager.Instance?.ShowUnitInfo(clicked);
                            AudioManager.Instance?.PlaySelectSound();
                        }
                        else if (selectedUnits.Count > 0)
                        {
                            // 攻击命令
                            foreach (var u in selectedUnits)
                                if (u != null && !u.def.isBuilding && !u.isHarvester)
                                    u.CommandAttack(clicked);
                            VFXManager.Instance?.SpawnAttackIndicator(worldPos);
                            AudioManager.Instance?.PlayMoveSound();
                        }
                        return;
                    }

                    // 点击空地 — 如果有选中单位，下达移动命令
                    if (selectedUnits.Count > 0)
                    {
                        bool hasMovable = false;
                        foreach (var u in selectedUnits)
                        {
                            if (u == null) continue;
                            if (!u.def.isBuilding && !u.isHarvester)
                            {
                                u.CommandMove(worldPos);
                                hasMovable = true;
                            }
                        }
                        if (hasMovable)
                        {
                            VFXManager.Instance?.SpawnMoveIndicator(worldPos);
                            AudioManager.Instance?.PlayMoveSound();
                        }
                        return;
                    }

                    // 无选中 — 准备拖动或框选
                    boxStartWorld = worldPos;
                    break;

                case TouchPhase.Moved:
                    float moveDist = Vector2.Distance(touch.position, touchStartPos);
                    if (!isBoxSelecting && moveDist > tapMoveThreshold)
                    {
                        if (Time.time - touchStartTime > longPressTime)
                        {
                            isBoxSelecting = true;
                        }
                        else
                        {
                            isPanning = true;
                        }
                    }

                    if (isPanning)
                    {
                        Vector2 delta = touch.deltaPosition;
                        mainCamera.transform.position -= new Vector3(delta.x * 0.01f * mainCamera.orthographicSize / 10, delta.y * 0.01f * mainCamera.orthographicSize / 10, 0);
                        ClampCamera();
                    }
                    break;

                case TouchPhase.Ended:
                    if (isBoxSelecting)
                    {
                        Vector3 boxEndWorld = mainCamera.ScreenToWorldPoint(touch.position);
                        boxEndWorld.z = 0;
                        PerformBoxSelect(boxStartWorld, boxEndWorld);
                    }
                    isBoxSelecting = false;
                    isPanning = false;
                    break;
            }
        }

        // 双指缩放
        if (Input.touchCount == 2)
        {
            Touch t1 = Input.GetTouch(0);
            Touch t2 = Input.GetTouch(1);

            float currentDist = Vector2.Distance(t1.position, t2.position);
            float prevDist = Vector2.Distance(t1.position - t1.deltaPosition, t2.position - t2.deltaPosition);

            float zoomDelta = (prevDist - currentDist) * zoomSpeed * 0.01f;
            mainCamera.orthographicSize = Mathf.Clamp(mainCamera.orthographicSize + zoomDelta, minZoom, maxZoom);
        }

        // 键盘编队（编辑器调试用）
        for (int i = 0; i < 5; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                    AssignSquad(i);
                else
                    SelectSquad(i);
            }
        }
    }

    void HandleCameraEdges()
    {
        // PC 调试用：鼠标边缘推屏
        if (Input.touchCount > 0) return;

        Vector3 move = Vector3.zero;
        Vector3 mousePos = Input.mousePosition;

        if (mousePos.x <= 5) move.x -= 1;
        if (mousePos.x >= Screen.width - 5) move.x += 1;
        if (mousePos.y <= 5) move.y -= 1;
        if (mousePos.y >= Screen.height - 5) move.y += 1;

        if (move != Vector3.zero)
        {
            mainCamera.transform.position += move.normalized * cameraMoveSpeed * Time.deltaTime;
            ClampCamera();
        }
    }

    void ClampCamera()
    {
        Vector3 pos = mainCamera.transform.position;
        float halfH = mainCamera.orthographicSize;
        float halfW = halfH * mainCamera.aspect;

        pos.x = Mathf.Clamp(pos.x, -edgeMargin, worldBounds.x - halfW + edgeMargin);
        pos.y = Mathf.Clamp(pos.y, -edgeMargin, worldBounds.y - halfH + edgeMargin);
        pos.z = -10;
        mainCamera.transform.position = pos;
    }

    UnitEntity PickUnit(Vector3 worldPos)
    {
        foreach (var unit in UnitRegistry.Instance.GetAllUnits())
        {
            if (unit == null || unit.isDead) continue;

            if (unit.def.isBuilding)
            {
                Vector2 size = unit.def.buildingSize;
                if (Mathf.Abs(worldPos.x - unit.transform.position.x) < size.x / 2 &&
                    Mathf.Abs(worldPos.y - unit.transform.position.y) < size.y / 2)
                    return unit;
            }
            else
            {
                float dist = Vector3.Distance(worldPos, unit.transform.position);
                if (dist < unit.def.selectionRadius + 8f)
                    return unit;
            }
        }
        return null;
    }

    void PerformBoxSelect(Vector3 start, Vector3 end)
    {
        ClearSelection();
        Vector3 min = Vector3.Min(start, end);
        Vector3 max = Vector3.Max(start, end);

        foreach (var unit in UnitRegistry.Instance.GetPlayerUnits())
        {
            if (unit == null || unit.isDead || unit.def.isBuilding || unit.isHarvester) continue;
            Vector3 pos = unit.transform.position;
            if (pos.x >= min.x && pos.x <= max.x && pos.y >= min.y && pos.y <= max.y)
            {
                selectedUnits.Add(unit);
            }
        }

        if (selectedUnits.Count > 0)
        {
            UIManager.Instance?.ShowUnitInfo(selectedUnits[0]);
            AudioManager.Instance?.PlaySelectSound();
        }
        else
        {
            UIManager.Instance?.HideUnitInfo();
        }
    }

    public void ClearSelection()
    {
        selectedUnits.Clear();
        UIManager.Instance?.HideUnitInfo();
    }

    public void AssignSquad(int slot)
    {
        var movable = selectedUnits.FindAll(u => u != null && !u.def.isBuilding && !u.isHarvester && !u.isDead);
        if (movable.Count == 0) { UIManager.Instance?.ShowToast("没有可编入的战斗单位"); return; }
        squads[slot] = new List<UnitEntity>(movable);
        UIManager.Instance?.ShowToast($"已编入编队 {slot + 1}（{movable.Count}个单位）");
        AudioManager.Instance?.PlaySelectSound();
    }

    public void SelectSquad(int slot)
    {
        if (squads[slot] == null || squads[slot].Count == 0) return;
        ClearSelection();
        selectedUnits.AddRange(squads[slot].FindAll(u => u != null && !u.isDead));
        if (selectedUnits.Count > 0)
        {
            UIManager.Instance?.ShowUnitInfo(selectedUnits[0]);
            // 镜头跳到编队中心
            Vector3 center = Vector3.zero;
            foreach (var u in selectedUnits) center += u.transform.position;
            center /= selectedUnits.Count;
            mainCamera.transform.position = new Vector3(center.x, center.y, -10);
            ClampCamera();
        }
    }
}
