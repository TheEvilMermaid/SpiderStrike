using UnityEngine;

/// <summary>
/// Looks at the X/Y play plane from positive Z. Z is presentation only.
/// </summary>
public class GameCamera : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] Vector3 offset = new Vector3(0f, 1.4f, 14f);
    [SerializeField] float smooth = 8f;

    public Transform Target
    {
        get => target;
        set => target = value;
    }

    void LateUpdate()
    {
        if (target == null)
            return;

        Vector3 desired = new Vector3(target.position.x, target.position.y, 0f) + offset;
        float blend = 1f - Mathf.Exp(-smooth * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, desired, blend);
        transform.rotation = Quaternion.Euler(0f, 180f, 0f);
    }
}
