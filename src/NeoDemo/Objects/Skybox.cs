﻿using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Veldrid.Utilities;

namespace Veldrid.NeoDemo.Objects
{
    public class Skybox : Renderable
    {
        private readonly Image<Rgba32> _front;
        private readonly Image<Rgba32> _back;
        private readonly Image<Rgba32> _left;
        private readonly Image<Rgba32> _right;
        private readonly Image<Rgba32> _top;
        private readonly Image<Rgba32> _bottom;

        // Context objects
        private DeviceBuffer _vb;
        private DeviceBuffer _ib;
        private Pipeline _pipeline;
        private Pipeline _reflectionPipeline;
        private ResourceSet _resourceSet;
        private readonly DisposeCollector _disposeCollector = new DisposeCollector();

        public Skybox(
            Image<Rgba32> front, Image<Rgba32> back, Image<Rgba32> left,
            Image<Rgba32> right, Image<Rgba32> top, Image<Rgba32> bottom)
        {
            _front = front;
            _back = back;
            _left = left;
            _right = right;
            _top = top;
            _bottom = bottom;
        }

        public unsafe override void CreateDeviceObjects(GraphicsDevice gd, CommandList cl, SceneContext sc)
        {
            ResourceFactory factory = gd.ResourceFactory;

            _vb = factory.CreateBuffer(new BufferDescription(s_vertices.SizeInBytes(), BufferUsage.VertexBuffer));
            cl.UpdateBuffer(_vb, 0, s_vertices);

            _ib = factory.CreateBuffer(new BufferDescription(s_indices.SizeInBytes(), BufferUsage.IndexBuffer));
            cl.UpdateBuffer(_ib, 0, s_indices);

            Texture textureCube;
            TextureView textureView;
            fixed (Rgba32* frontPin = &_front.DangerousGetPinnableReferenceToPixelBuffer())
            fixed (Rgba32* backPin = &_back.DangerousGetPinnableReferenceToPixelBuffer())
            fixed (Rgba32* leftPin = &_left.DangerousGetPinnableReferenceToPixelBuffer())
            fixed (Rgba32* rightPin = &_right.DangerousGetPinnableReferenceToPixelBuffer())
            fixed (Rgba32* topPin = &_top.DangerousGetPinnableReferenceToPixelBuffer())
            fixed (Rgba32* bottomPin = &_bottom.DangerousGetPinnableReferenceToPixelBuffer())
            {
                uint width = (uint)_front.Width;
                uint height = (uint)_front.Height;
                textureCube = factory.CreateTexture(TextureDescription.Texture2D(
                    width,
                    height,
                    1,
                    1,
                    PixelFormat.R8_G8_B8_A8_UNorm,
                    TextureUsage.Sampled | TextureUsage.Cubemap));

                uint faceSize = (uint)(_front.Width * _front.Height * Unsafe.SizeOf<Rgba32>());
                gd.UpdateTexture(textureCube, (IntPtr)rightPin, faceSize, 0, 0, 0, width, height, 1, 0, 0);
                gd.UpdateTexture(textureCube, (IntPtr)leftPin, faceSize, 0, 0, 0, width, height, 1, 0, 1);
                gd.UpdateTexture(textureCube, (IntPtr)topPin, faceSize, 0, 0, 0, width, height, 1, 0, 2);
                gd.UpdateTexture(textureCube, (IntPtr)bottomPin, faceSize, 0, 0, 0, width, height, 1, 0, 3);
                gd.UpdateTexture(textureCube, (IntPtr)backPin, faceSize, 0, 0, 0, width, height, 1, 0, 4);
                gd.UpdateTexture(textureCube, (IntPtr)frontPin, faceSize, 0, 0, 0, width, height, 1, 0, 5);

                textureView = factory.CreateTextureView(new TextureViewDescription(textureCube));
            }

            VertexLayoutDescription[] vertexLayouts = new VertexLayoutDescription[]
            {
                new VertexLayoutDescription(
                    new VertexElementDescription("Position", VertexElementSemantic.Position, VertexElementFormat.Float3))
            };

            Shader vs = ShaderHelper.LoadShader(gd, factory, "Skybox", ShaderStages.Vertex, "VS");
            Shader fs = ShaderHelper.LoadShader(gd, factory, "Skybox", ShaderStages.Fragment, "FS");

            _layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Projection", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("View", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("CubeTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("CubeSampler", ResourceKind.Sampler, ShaderStages.Fragment)));

            GraphicsPipelineDescription pd = new GraphicsPipelineDescription(
                BlendStateDescription.SingleAlphaBlend,
                DepthStencilStateDescription.DepthOnlyLessEqual,
                new RasterizerStateDescription(FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, true, true),
                PrimitiveTopology.TriangleList,
                new ShaderSetDescription(vertexLayouts, new[] { vs, fs }),
                new ResourceLayout[] { _layout },
                sc.MainSceneFramebuffer.OutputDescription);

            _pipeline = factory.CreateGraphicsPipeline(ref pd);
            pd.Outputs = sc.ReflectionFramebuffer.OutputDescription;
            _reflectionPipeline = factory.CreateGraphicsPipeline(ref pd);

            _resourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                _layout,
                sc.ProjectionMatrixBuffer,
                sc.ViewMatrixBuffer,
                textureView,
                gd.PointSampler));

            _disposeCollector.Add(_vb, _ib, textureCube, textureView, _layout, _pipeline, _reflectionPipeline, _resourceSet, vs, fs);
        }

        public override void UpdatePerFrameResources(GraphicsDevice gd, CommandList cl, SceneContext sc)
        {
        }

        public static Skybox LoadDefaultSkybox()
        {
            return new Skybox(
                Image.Load(AssetHelper.GetPath("Textures/cloudtop/cloudtop_ft.png")),
                Image.Load(AssetHelper.GetPath("Textures/cloudtop/cloudtop_bk.png")),
                Image.Load(AssetHelper.GetPath("Textures/cloudtop/cloudtop_lf.png")),
                Image.Load(AssetHelper.GetPath("Textures/cloudtop/cloudtop_rt.png")),
                Image.Load(AssetHelper.GetPath("Textures/cloudtop/cloudtop_up.png")),
                Image.Load(AssetHelper.GetPath("Textures/cloudtop/cloudtop_dn.png")));
        }

        public override void DestroyDeviceObjects()
        {
            _disposeCollector.DisposeAll();
        }

        public override void Render(GraphicsDevice gd, CommandList cl, SceneContext sc, RenderPasses renderPass)
        {
            cl.SetVertexBuffer(0, _vb);
            cl.SetIndexBuffer(_ib, IndexFormat.UInt16);
            cl.SetPipeline(renderPass == RenderPasses.ReflectionMap ? _reflectionPipeline : _pipeline);
            cl.SetGraphicsResourceSet(0, _resourceSet);
            cl.DrawIndexed((uint)s_indices.Length, 1, 0, 0, 0);
        }

        public override RenderPasses RenderPasses => RenderPasses.Standard | RenderPasses.ReflectionMap;

        public override RenderOrderKey GetRenderOrderKey(Vector3 cameraPosition)
        {
            return new RenderOrderKey(ulong.MaxValue);
        }

        private static readonly VertexPosition[] s_vertices = new VertexPosition[]
        {
            // Top
            new VertexPosition(new Vector3(-20.0f,20.0f,-20.0f)),
            new VertexPosition(new Vector3(20.0f,20.0f,-20.0f)),
            new VertexPosition(new Vector3(20.0f,20.0f,20.0f)),
            new VertexPosition(new Vector3(-20.0f,20.0f,20.0f)),
            // Bottom
            new VertexPosition(new Vector3(-20.0f,-20.0f,20.0f)),
            new VertexPosition(new Vector3(20.0f,-20.0f,20.0f)),
            new VertexPosition(new Vector3(20.0f,-20.0f,-20.0f)),
            new VertexPosition(new Vector3(-20.0f,-20.0f,-20.0f)),
            // Left
            new VertexPosition(new Vector3(-20.0f,20.0f,-20.0f)),
            new VertexPosition(new Vector3(-20.0f,20.0f,20.0f)),
            new VertexPosition(new Vector3(-20.0f,-20.0f,20.0f)),
            new VertexPosition(new Vector3(-20.0f,-20.0f,-20.0f)),
            // Right
            new VertexPosition(new Vector3(20.0f,20.0f,20.0f)),
            new VertexPosition(new Vector3(20.0f,20.0f,-20.0f)),
            new VertexPosition(new Vector3(20.0f,-20.0f,-20.0f)),
            new VertexPosition(new Vector3(20.0f,-20.0f,20.0f)),
            // Back
            new VertexPosition(new Vector3(20.0f,20.0f,-20.0f)),
            new VertexPosition(new Vector3(-20.0f,20.0f,-20.0f)),
            new VertexPosition(new Vector3(-20.0f,-20.0f,-20.0f)),
            new VertexPosition(new Vector3(20.0f,-20.0f,-20.0f)),
            // Front
            new VertexPosition(new Vector3(-20.0f,20.0f,20.0f)),
            new VertexPosition(new Vector3(20.0f,20.0f,20.0f)),
            new VertexPosition(new Vector3(20.0f,-20.0f,20.0f)),
            new VertexPosition(new Vector3(-20.0f,-20.0f,20.0f)),
        };

        private static readonly ushort[] s_indices = new ushort[]
        {
            0,1,2, 0,2,3,
            4,5,6, 4,6,7,
            8,9,10, 8,10,11,
            12,13,14, 12,14,15,
            16,17,18, 16,18,19,
            20,21,22, 20,22,23,
        };
        private ResourceLayout _layout;
    }
}
