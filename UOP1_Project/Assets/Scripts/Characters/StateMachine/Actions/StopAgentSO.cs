using UnityEngine;
using UnityEngine.AI;
using UOP1.StateMachine;
using UOP1.StateMachine.ScriptableObjects;

[CreateAssetMenu(fileName = "StopAgent", menuName = "State Machines/Actions/Stop NavMesh Agent")]
public class StopAgentSO : StateActionSO
{
    protected override StateAction CreateAction() => new StopAgent();
}

public class StopAgent : StateAction
{
    private NavMeshAgent _agent;
    private bool _agentDefined;

    public override void Awake(StateMachine stateMachine)
    {
        _agent = stateMachine.gameObject.GetComponent<NavMeshAgent>();
        _agentDefined = _agent != null;
    }

    public override void OnUpdate()
    {
        // Để trống
    }

    public override void OnStateEnter()
    {
        // KHIÊN BẢO VỆ CHỐNG CRASH Ở ĐÂY
        if (_agentDefined && _agent.isActiveAndEnabled && _agent.isOnNavMesh)
        {
            _agent.isStopped = true;
        }
    }
}