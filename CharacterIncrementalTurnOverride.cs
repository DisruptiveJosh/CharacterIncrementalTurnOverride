using System;
using GameCreator.Runtime.Characters;
using GameCreator.Runtime.Common;
using UnityEngine;

namespace SpacesSDK.GameCreatorExtensions
{
    [AddComponentMenu("Game Creator/Characters/Incremental Turn Override")]
    [DisallowMultipleComponent]
    public class CharacterIncrementalTurnOverride : MonoBehaviour
    {
        private const float INPUT_THRESHOLD = 0.0001f;

        [SerializeField] private Character m_Character;
        [SerializeField, Range(0f, 1f)] private float m_RotationSmoothing = 1f;
        [SerializeField, Range(0.01f, 0.5f)] private float m_RotationSmoothTime = 0.12f;
        [SerializeField, Min(0f)] private float m_TurnSpeedDegrees = 180f;
        [SerializeField] private string m_HorizontalAxis = "Horizontal";
        [SerializeField] private string m_VerticalAxis = "Vertical";
        [SerializeField] private Axonometry m_Axonometry = new Axonometry();

        private TUnitFacing m_PreviousFacing;
        private TUnitPlayer m_PreviousPlayer;
        private SmoothFacingUnit m_CustomFacing;
        private DirectionalTurnOnlyPlayer m_CustomPlayer;
        private bool m_IsApplied;

        private void Reset()
        {
            m_Character = GetComponent<Character>();
        }

        private void Awake()
        {
            if (Application.isPlaying)
            {
                ApplyOverrides();
            }
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                ApplyOverrides();
            }
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
            {
                RestoreOverrides();
            }
        }

        private void OnDestroy()
        {
            if (Application.isPlaying)
            {
                RestoreOverrides();
            }
        }

        private void OnValidate()
        {
            if (m_RotationSmoothing < 0f) m_RotationSmoothing = 0f;
            if (m_RotationSmoothing > 1f) m_RotationSmoothing = 1f;
            if (m_RotationSmoothTime < 0.01f) m_RotationSmoothTime = 0.01f;
            if (m_TurnSpeedDegrees < 0f) m_TurnSpeedDegrees = 0f;

            m_CustomFacing?.Configure(
                m_RotationSmoothing,
                m_RotationSmoothTime,
                m_TurnSpeedDegrees,
                m_HorizontalAxis,
                m_Axonometry
            );

            m_CustomPlayer?.Configure(m_VerticalAxis);
        }

        private void ApplyOverrides()
        {
            if (m_Character == null) m_Character = GetComponent<Character>();
            if (m_Character == null)
            {
                Debug.LogWarning($"{nameof(CharacterIncrementalTurnOverride)} requires a Character component on the same GameObject.", this);
                return;
            }

            CharacterKernel kernel = m_Character.Kernel;
            if (kernel == null)
            {
                Debug.LogWarning("Unable to access the Character kernel to override facing.", this);
                return;
            }

            if (m_CustomFacing == null) m_CustomFacing = new SmoothFacingUnit();
            if (m_CustomPlayer == null) m_CustomPlayer = new DirectionalTurnOnlyPlayer();

            m_CustomFacing.Configure(
                m_RotationSmoothing,
                m_RotationSmoothTime,
                m_TurnSpeedDegrees,
                m_HorizontalAxis,
                m_Axonometry
            );

            m_CustomPlayer.Configure(m_VerticalAxis);

            if (!m_IsApplied)
            {
                m_PreviousFacing = kernel.Facing as TUnitFacing;
                m_PreviousPlayer = kernel.Player as TUnitPlayer;
            }

            if (!ReferenceEquals(kernel.Player, m_CustomPlayer))
            {
                kernel.ChangePlayer(m_Character, m_CustomPlayer);
            }

            if (!ReferenceEquals(kernel.Facing, m_CustomFacing))
            {
                kernel.ChangeFacing(m_Character, m_CustomFacing);
            }

            m_IsApplied = true;
        }

        private void RestoreOverrides()
        {
            if (!m_IsApplied) return;
            if (m_Character == null) return;

            CharacterKernel kernel = m_Character.Kernel;
            if (kernel == null) return;

            if (ReferenceEquals(kernel.Player, m_CustomPlayer) && m_PreviousPlayer != null)
            {
                kernel.ChangePlayer(m_Character, m_PreviousPlayer);
            }

            if (ReferenceEquals(kernel.Facing, m_CustomFacing) && m_PreviousFacing != null)
            {
                kernel.ChangeFacing(m_Character, m_PreviousFacing);
            }

            m_CustomFacing?.ResetState();
            m_CustomPlayer?.ResetState();

            m_IsApplied = false;
        }

        [Serializable]
        private class SmoothFacingUnit : TUnitFacing
        {
            [SerializeField] private Axonometry m_InternalAxonometry = new Axonometry();

            private float m_RotationSmoothing = 1f;
            private float m_RotationSmoothTime = 0.12f;
            private float m_TurnSpeedDegrees = 180f;
            private string m_HorizontalAxis = "Horizontal";
            private float m_RotationVelocity;
            private float m_TargetYaw;
            private Vector3 m_CurrentDirection = Vector3.forward;

            public override Axonometry Axonometry
            {
                get => m_InternalAxonometry;
                set => m_InternalAxonometry = value;
            }

            public void Configure(
                float rotationSmoothing,
                float rotationSmoothTime,
                float turnSpeedDegrees,
                string horizontalAxis,
                Axonometry axonometry
            )
            {
                m_RotationSmoothing = Mathf.Clamp01(rotationSmoothing);
                m_RotationSmoothTime = Mathf.Max(0.01f, rotationSmoothTime);
                m_TurnSpeedDegrees = Mathf.Max(0f, turnSpeedDegrees);
                m_HorizontalAxis = string.IsNullOrWhiteSpace(horizontalAxis) ? "Horizontal" : horizontalAxis;

                Axonometry clone = axonometry != null ? axonometry.Clone() as Axonometry : null;
                m_InternalAxonometry = clone ?? new Axonometry();
            }

            public void ResetState()
            {
                m_RotationVelocity = 0f;
                m_CurrentDirection = Transform != null ? Transform.forward : Vector3.forward;
                m_TargetYaw = Transform != null ? Transform.eulerAngles.y : 0f;
            }

            public override void OnStartup(Character character)
            {
                base.OnStartup(character);
                ResetState();
            }

            public override void OnUpdate()
            {
                UpdateDirection();
                base.OnUpdate();
            }

            protected override Vector3 GetDefaultDirection()
            {
                Vector3 heading = m_CurrentDirection;
                return m_InternalAxonometry?.ProcessRotation(this, heading) ?? heading;
            }

            private void UpdateDirection()
            {
                if (Character == null || Transform == null) return;

                float deltaTime = Character.Time.DeltaTime;
                float horizontal = ReadAxis(m_HorizontalAxis);

                if (Mathf.Abs(horizontal) > INPUT_THRESHOLD && m_TurnSpeedDegrees > 0f)
                {
                    m_TargetYaw += horizontal * m_TurnSpeedDegrees * deltaTime;
                }

                m_TargetYaw = Mathf.Repeat(m_TargetYaw + 360f, 360f);

                float currentYaw = Transform.eulerAngles.y;
                float rotation;
                if (m_RotationSmoothing <= 0f)
                {
                    rotation = m_TargetYaw;
                    m_RotationVelocity = 0f;
                }
                else
                {
                    float effectiveSmoothTime = m_RotationSmoothTime * m_RotationSmoothing;
                    if (effectiveSmoothTime < 0.0001f) effectiveSmoothTime = 0.0001f;

                    rotation = Mathf.SmoothDampAngle(
                        currentYaw,
                        m_TargetYaw,
                        ref m_RotationVelocity,
                        effectiveSmoothTime,
                        Mathf.Infinity,
                        deltaTime
                    );
                }

                m_CurrentDirection = Quaternion.Euler(0f, rotation, 0f) * Vector3.forward;
            }

            private float ReadAxis(string axis)
            {
                return string.IsNullOrEmpty(axis) ? 0f : Input.GetAxisRaw(axis);
            }
        }

        [Serializable]
        private class DirectionalTurnOnlyPlayer : TUnitPlayer
        {
            private string m_VerticalAxis = "Vertical";

            public void Configure(string verticalAxis)
            {
                m_VerticalAxis = string.IsNullOrWhiteSpace(verticalAxis) ? "Vertical" : verticalAxis;
            }

            public void ResetState()
            {
                InputDirection = Vector3.zero;
                if (Character != null)
                {
                    Character.Motion?.MoveToDirection(Vector3.zero, Space.World, 0);
                }
            }

            public override void OnDisable()
            {
                base.OnDisable();
                ResetState();
            }

            public override void OnUpdate()
            {
                base.OnUpdate();

                InputDirection = Vector3.zero;
                if (Character == null) return;
                if (!Character.IsPlayer) return;
                if (!m_IsControllable) return;

                float vertical = string.IsNullOrEmpty(m_VerticalAxis)
                    ? 0f
                    : Input.GetAxisRaw(m_VerticalAxis);

                float magnitude = Mathf.Clamp01(Mathf.Abs(vertical));
                if (magnitude <= INPUT_THRESHOLD)
                {
                    Character.Motion?.MoveToDirection(Vector3.zero, Space.World, 0);
                    return;
                }

                Vector3 forward = Transform != null ? Transform.forward : Vector3.forward;
                forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
                if (forward.sqrMagnitude <= INPUT_THRESHOLD) forward = Vector3.forward;

                Vector3 direction = forward * Mathf.Sign(vertical);
                InputDirection = direction * magnitude;

                float speed = Character.Motion?.LinearSpeed ?? 0f;
                Character.Motion?.MoveToDirection(InputDirection * speed, Space.World, 0);
            }
        }
    }
}
