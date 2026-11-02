// ================================================================
// HarvesterController.cs — 采矿车完整逻辑
// 状态机：idle → moving_to_ore → harvesting → returning_to_refinery → depositing
// ================================================================
using UnityEngine;

public class HarvesterController : MonoBehaviour
{
    [Header("Config")]
    public float harvestTime = 2f;
    public int oreCapacity = 100;
    public float depositRange = 30f;

    [Header("Runtime")]
    public OreNode targetNode;
    public UnitEntity refinery; // 挂靠的矿场
    public int oreCarried = 0;

    private UnitEntity entity;
    private enum State { Idle, MovingToOre, Harvesting, Returning, Depositing }
    private State state = State.Idle;
    private float timer = 0f;

    void Awake()
    {
        entity = GetComponent<UnitEntity>();
    }

    void Start()
    {
        // 自动找最近矿点
        if (targetNode == null)
            FindNearestOreNode();
    }

    void Update()
    {
        if (entity == null || entity.isDead) return;

        float dt = GameClock.Instance?.DeltaTime ?? Time.deltaTime;

        switch (state)
        {
            case State.Idle:
                FindNearestOreNode();
                break;

            case State.MovingToOre:
                if (targetNode == null || targetNode.IsDepleted())
                {
                    state = State.Idle;
                    break;
                }
                float distToOre = Vector3.Distance(transform.position, targetNode.transform.position);
                if (distToOre < targetNode.radius)
                {
                    state = State.Harvesting;
                    timer = harvestTime;
                }
                else
                {
                    MoveTo(targetNode.transform.position, dt);
                }
                break;

            case State.Harvesting:
                timer -= dt;
                if (timer <= 0)
                {
                    oreCarried = targetNode.Harvest(oreCapacity);
                    state = State.Returning;
                    FindNearestRefinery();
                }
                break;

            case State.Returning:
                if (refinery == null || refinery.isDead)
                {
                    FindNearestRefinery();
                    if (refinery == null) break;
                }
                float distToRef = Vector3.Distance(transform.position, refinery.transform.position);
                if (distToRef < depositRange)
                {
                    state = State.Depositing;
                    timer = 0.5f;
                }
                else
                {
                    MoveTo(refinery.transform.position, dt);
                }
                break;

            case State.Depositing:
                timer -= dt;
                if (timer <= 0)
                {
                    EconomySystem.Instance.DepositOre(oreCarried, entity.isEnemy);
                    oreCarried = 0;
                    state = State.MovingToOre;
                }
                break;
        }
    }

    void FindNearestOreNode()
    {
        OreNode[] nodes = FindObjectsByType<OreNode>(FindObjectsSortMode.None);
        OreNode nearest = null;
        float minDist = float.MaxValue;

        foreach (var node in nodes)
        {
            if (node.IsDepleted()) continue;
            float dist = Vector3.Distance(transform.position, node.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                nearest = node;
            }
        }

        if (nearest != null)
        {
            targetNode = nearest;
            state = State.MovingToOre;
        }
    }

    void FindNearestRefinery()
    {
        var units = entity.isEnemy ? UnitRegistry.Instance.GetEnemyUnits() : UnitRegistry.Instance.GetPlayerUnits();
        UnitEntity nearest = null;
        float minDist = float.MaxValue;

        foreach (var u in units)
        {
            if (u == null || u.isDead) continue;
            if (u.def.unitId == "refinery")
            {
                float dist = Vector3.Distance(transform.position, u.transform.position);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = u;
                }
            }
        }

        refinery = nearest;
    }

    void MoveTo(Vector3 target, float dt)
    {
        Vector3 toTarget = target - transform.position;
        float dist = toTarget.magnitude;

        if (dist > 0.1f)
        {
            float speed = entity.def.moveSpeed * dt;

            // 优先用流场寻路
            if (FlowFieldPathfinding.Instance != null && FlowFieldPathfinding.Instance.IsReachable(target))
            {
                Vector2 flowDir = FlowFieldPathfinding.Instance.GetDirection(transform.position);
                if (flowDir != Vector2.zero)
                {
                    transform.position += (Vector3)flowDir * speed;
                    entity.facing = Mathf.Atan2(flowDir.y, flowDir.x);
                    return;
                }
            }

            // 回退：直线移动
            transform.position += toTarget.normalized * speed;
            entity.facing = Mathf.Atan2(toTarget.y, toTarget.x);
        }
    }

    /// <summary>
    /// 玩家手动指定矿点
    /// </summary>
    public void SetTargetNode(OreNode node)
    {
        targetNode = node;
        state = State.MovingToOre;
    }
}
