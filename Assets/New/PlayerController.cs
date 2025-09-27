using System;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 5f;
    public float lookSpeed = 2f;
    public Transform cameraTransform;
    private Rigidbody _rb;
    private float pitch = 0f;

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        _rb = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        HandleMovement();
        HandleCamera();
    }

    private void HandleMovement()
    {
        // Get input
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        float y = (Input.GetKey(KeyCode.Q) ? 1 : 0) - (Input.GetKey(KeyCode.E) ? 1 : 0);

        // Relative to camera
        Vector3 forward = cameraTransform.forward;
        forward.y = 0; // ignore vertical for horizontal movement
        forward.Normalize();
        Vector3 right = cameraTransform.right;

        Vector3 move = forward * v + right * h + Vector3.up * y;
        _rb.linearVelocity = move * moveSpeed;
    }

    private void HandleCamera()
    {
        float mouseX = Input.GetAxis("Mouse X") * lookSpeed;
        float mouseY = Input.GetAxis("Mouse Y") * lookSpeed;

        // Rotate player horizontally
        transform.Rotate(Vector3.up * mouseX);

        // Rotate camera vertically
        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, -80f, 80f);
        cameraTransform.localEulerAngles = new Vector3(pitch, 0f, 0f);
    }
}