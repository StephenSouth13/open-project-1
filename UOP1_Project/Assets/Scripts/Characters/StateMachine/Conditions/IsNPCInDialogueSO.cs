using UnityEngine;
using UOP1.StateMachine;
using UOP1.StateMachine.ScriptableObjects;

[CreateAssetMenu(menuName = "State Machines/Conditions/Is NPC In Dialogue")]
public class IsNPCInDialogueSO : StateConditionSO<IsNPCDialogueCondition> { }

public class IsNPCDialogueCondition : Condition
{
    //Component references
    private StepController _stepControllerScript;

    public override void Awake(StateMachine stateMachine)
    {
        // 🛡️ Đổi thành stateMachine.gameObject.GetComponent để né hàm gây Crash của Chop Chop
        _stepControllerScript = stateMachine.gameObject.GetComponent<StepController>();
    }

    protected override bool Statement()
    {
        // 🛡️ Nếu NPC không có StepController (như con Gemini), bỏ qua luôn
        if (_stepControllerScript == null)
        {
            return false;
        }

        return _stepControllerScript.isInDialogue;
    }
}
