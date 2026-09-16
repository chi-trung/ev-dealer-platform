import { useRef, Suspense } from 'react'
import { Canvas, useFrame } from '@react-three/fiber'
import { OrbitControls, PerspectiveCamera, Environment } from '@react-three/drei'
import * as THREE from 'three'

// Simple 3D Car Model using primitives
function Car() {
  const carRef = useRef()

  useFrame((state) => {
    // Gentle rotation animation
    if (carRef.current) {
      carRef.current.rotation.y = Math.sin(state.clock.elapsedTime * 0.3) * 0.2
    }
  })

  return (
    <group ref={carRef} position={[0, -0.5, 0]}>
      {/* Car Body - Main */}
      <mesh position={[0, 0.4, 0]} castShadow>
        <boxGeometry args={[2.5, 0.8, 1.2]} />
        <meshStandardMaterial 
          color="#667eea" 
          metalness={0.8} 
          roughness={0.2}
          envMapIntensity={1}
        />
      </mesh>

      {/* Car Roof */}
      <mesh position={[0.2, 0.95, 0]} castShadow>
        <boxGeometry args={[1.2, 0.7, 1.15]} />
        <meshStandardMaterial 
          color="#667eea" 
          metalness={0.8} 
          roughness={0.2}
          envMapIntensity={1}
        />
      </mesh>

      {/* Front Window */}
      <mesh position={[0.8, 0.95, 0]}>
        <boxGeometry args={[0.1, 0.6, 1.1]} />
        <meshStandardMaterial 
          color="#1a1f3a" 
          metalness={0.9} 
          roughness={0.1}
          transparent
          opacity={0.3}
        />
      </mesh>

      {/* Back Window */}
      <mesh position={[-0.4, 0.95, 0]}>
        <boxGeometry args={[0.1, 0.6, 1.1]} />
        <meshStandardMaterial 
          color="#1a1f3a" 
          metalness={0.9} 
          roughness={0.1}
          transparent
          opacity={0.3}
        />
      </mesh>

      {/* Wheels */}
      {[
        [-0.8, 0, 0.7],
        [-0.8, 0, -0.7],
        [0.8, 0, 0.7],
        [0.8, 0, -0.7],
      ].map((position, index) => (
        <group key={index} position={position}>
          <mesh rotation={[0, 0, Math.PI / 2]} castShadow>
            <cylinderGeometry args={[0.35, 0.35, 0.3, 32]} />
            <meshStandardMaterial color="#1a1a1a" metalness={0.5} roughness={0.5} />
          </mesh>
          {/* Wheel rim */}
          <mesh rotation={[0, 0, Math.PI / 2]} position={[0, 0, 0]}>
            <cylinderGeometry args={[0.2, 0.2, 0.32, 32]} />
            <meshStandardMaterial color="#c0c0c0" metalness={0.9} roughness={0.1} />
          </mesh>
        </group>
      ))}

      {/* Headlights */}
      <mesh position={[1.26, 0.4, 0.4]} castShadow>
        <boxGeometry args={[0.05, 0.2, 0.3]} />
        <meshStandardMaterial 
          color="#ffffff" 
          emissive="#4facfe"
          emissiveIntensity={0.5}
        />
      </mesh>
      <mesh position={[1.26, 0.4, -0.4]} castShadow>
        <boxGeometry args={[0.05, 0.2, 0.3]} />
        <meshStandardMaterial 
          color="#ffffff" 
          emissive="#4facfe"
          emissiveIntensity={0.5}
        />
      </mesh>

      {/* Taillights */}
      <mesh position={[-1.26, 0.4, 0.4]} castShadow>
        <boxGeometry args={[0.05, 0.15, 0.25]} />
        <meshStandardMaterial 
          color="#ff0000" 
          emissive="#ff0000"
          emissiveIntensity={0.3}
        />
      </mesh>
      <mesh position={[-1.26, 0.4, -0.4]} castShadow>
        <boxGeometry args={[0.05, 0.15, 0.25]} />
        <meshStandardMaterial 
          color="#ff0000" 
          emissive="#ff0000"
          emissiveIntensity={0.3}
        />
      </mesh>

      {/* Undercarriage */}
      <mesh position={[0, 0, 0]} receiveShadow>
        <boxGeometry args={[2.3, 0.1, 1.1]} />
        <meshStandardMaterial color="#2a2a2a" metalness={0.3} roughness={0.7} />
      </mesh>
    </group>
  )
}

// Main 3D Scene Component
export default function Car3D() {
  return (
    <Canvas
      shadows
      style={{ 
        width: '100%', 
        height: '100%',
        background: 'transparent',
      }}
      gl={{ 
        alpha: true,
        antialias: true,
        toneMapping: THREE.ACESFilmicToneMapping,
        toneMappingExposure: 1.2,
      }}
    >
      {/* Camera */}
      <PerspectiveCamera makeDefault position={[5, 2, 5]} fov={50} />

      {/* Lights */}
      <ambientLight intensity={0.5} />
      <directionalLight 
        position={[10, 10, 5]} 
        intensity={1} 
        castShadow
        shadow-mapSize-width={2048}
        shadow-mapSize-height={2048}
      />
      <pointLight position={[-10, 5, -5]} intensity={0.5} color="#667eea" />
      <pointLight position={[10, 5, 5]} intensity={0.5} color="#f093fb" />

      {/* Environment for reflections (HDR loads async — scoped boundary keeps
          the car itself painted even if the fetch is slow; Issue #69) */}
      <Suspense fallback={null}>
        <Environment preset="city" />
      </Suspense>

      {/* Car Model */}
      <Car />

      {/* Ground plane for shadows */}
      <mesh rotation={[-Math.PI / 2, 0, 0]} position={[0, -0.5, 0]} receiveShadow>
        <planeGeometry args={[20, 20]} />
        <shadowMaterial opacity={0.3} />
      </mesh>

      {/* Controls */}
      <OrbitControls 
        enableZoom={true}
        enablePan={false}
        minDistance={3}
        maxDistance={10}
        minPolarAngle={Math.PI / 4}
        maxPolarAngle={Math.PI / 2}
        autoRotate
        autoRotateSpeed={0.5}
      />
    </Canvas>
  )
}

