using UnityEngine;
using UnityEngine.InputSystem;

// The demo's view: the camera circles a pivot. Left drag turns it, right or middle drag moves the
// pivot across the view, the wheel moves in and out. A drag that starts on the panel is the panel's.
[RequireComponent(typeof(Camera))]
public class orbit_camera : MonoBehaviour{
    public demo ui;
    public Vector3 pivot = new(0f, 1f, 0f);
    public float distance = 6.5f;
    public float yaw;
    public float pitch = 12f;

    const float turn_per_px = 0.25f;    // degrees
    const float pan_per_px = 0.0012f;   // of the distance, so the pivot follows the pointer at any zoom
    const float zoom_per_notch = 1.12f;

    // Whether the drag under way began on the panel; held until every button is up again.
    bool on_panel;

    void LateUpdate(){
        var mouse = Mouse.current;
        if(mouse != null){
            if(mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame){
                on_panel = ui.pointer_over_ui;
            }
            if(!mouse.leftButton.isPressed && !mouse.rightButton.isPressed && !mouse.middleButton.isPressed){
                on_panel = false;
            }

            var d = mouse.delta.ReadValue();
            if(mouse.leftButton.isPressed && !on_panel){
                yaw += d.x * turn_per_px;
                pitch = Mathf.Clamp(pitch - d.y * turn_per_px, -85f, 85f);
            }
            if((mouse.rightButton.isPressed || mouse.middleButton.isPressed) && !on_panel){
                pivot -= (transform.right * d.x + transform.up * d.y) * (pan_per_px * distance);
            }

            // A notch is 120 on Windows and other sizes in browsers, so only its sign counts.
            var notch = Mathf.Clamp(mouse.scroll.ReadValue().y, -1f, 1f);
            if(notch != 0f && !ui.pointer_over_ui){
                distance = Mathf.Clamp(distance * Mathf.Pow(zoom_per_notch, -notch), 1f, 30f);
            }
        }
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        transform.position = pivot - transform.forward * distance;
    }
}
