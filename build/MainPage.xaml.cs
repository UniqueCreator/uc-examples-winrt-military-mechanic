using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics.Display;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.System.Threading;

using UniqueCreator.Graphics.Gpu;

namespace uc_example
{

    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private ResourceCreateContext           m_ctx;
        private CompositionSwapChainResources   m_swapChain;
        private object m_rendererLock           = new object();
        private IAsyncAction                    m_renderLoopWorker;
        private DerivativesSkinnedMaterial      m_mechanicMaterial;
        private DerivativesSkinnedModel         m_mechanicModel;
        private DerivativesSkinnedModelInstance m_mechanicInstance;

        private ComputePipelineState            m_lighting;

        private Matrix44 identity()
        {
            Matrix44 r;
            r.r00 = 1.0f;
            r.r01 = 0.0f;
            r.r02 = 0.0f;
            r.r03 = 0.0f;

            r.r10 = 0.0f;
            r.r11 = 1.0f;
            r.r12 = 0.0f;
            r.r13 = 0.0f;

            r.r20 = 0.0f;
            r.r21 = 0.0f;
            r.r22 = 1.0f;
            r.r23 = 0.0f;

            r.r30 = 0.0f;
            r.r31 = 0.0f;
            r.r32 = 0.0f;
            r.r33 = 1.0f;

            return r;
        }

        private Matrix44 CameraMatrix()
        {
            Matrix44 r;
            r.r00 = -1.0f;
            r.r01 = 0.0f;
            r.r02 = 0.0f;
            r.r03 = 0.0f;

            r.r10 = 0.0f;
            r.r11 = 1.0f;
            r.r12 = 0.0f;
            r.r13 = 0.0f;

            r.r20 = 0.0f;
            r.r21 = 0.0f;
            r.r22 = -1.0f;
            r.r23 = 0.0f;

            r.r30 = 0.0f;
            r.r31 = 0.0f;
            r.r32 = 5.0f;
            r.r33 = 1.0f;

            return r;
        }


        private Matrix44 PerspectiveMatrix()
        {
            Matrix44 r;
            r.r00 = 1.35803974f;
            r.r01 = 0.0f;
            r.r02 = 0.0f;
            r.r03 = 0.0f;

            r.r10 = 0.0f;
            r.r11 = 2.41429281f;
            r.r12 = 0.0f;
            r.r13 = 0.0f;

            r.r20 = 0.0f;
            r.r21 = 0.0f;
            r.r22 = 1.00100100f;
            r.r23 = 1.0f;

            r.r30 = 0.0f;
            r.r31 = 0.0f;
            r.r32 = -0.100100100f;
            r.r33 = 0.0f;

            return r;
        }

        ComputePipelineState makeLighting(ResourceCreateContext ctx)
        {
            var code = UniqueCreator.Graphics.Gpu.Shaders.lighting_cs.Factory.Create();
            var description = new ComputePipelineStateDescription();
            description.CS = code;
            return new ComputePipelineState(m_ctx, description);
        }

        public MainPage()
        {
            this.InitializeComponent();
            
            m_ctx       = new ResourceCreateContext();
            m_swapChain = new CompositionSwapChainResources(m_ctx, m_swapChainPanel);

            var display = DisplayInformation.GetForCurrentView();
            display.DpiChanged += new TypedEventHandler<DisplayInformation, object>(OnDpiChanged);

            var ctx = m_swapChain.CreateGraphicsComputeCommandContext();

            m_mechanicMaterial  = new DerivativesSkinnedMaterial(m_ctx, ctx, "");
            m_mechanicModel     = new DerivativesSkinnedModel(m_mechanicMaterial, m_ctx, ctx, @"Assets\\models\\military_mechanic.derivatives_skinned_model.model");
            m_mechanicInstance  = new DerivativesSkinnedModelInstance(m_mechanicModel, identity());

            ctx.SubmitAndWaitToExecute();

            m_lighting = makeLighting(m_ctx);
        }

        public void OnResuming()
        {
            // If the animation render loop is already running then do not start another thread.
            if (m_renderLoopWorker != null && m_renderLoopWorker.Status == AsyncStatus.Started)
            {
                return;
            }

            // Create a task that will be run on a background thread.
            var workItemHandler = new WorkItemHandler((action) =>
            {
                // Calculate the updated frame and render once per vertical blanking interval.
                while (action.Status == AsyncStatus.Started)
                {
                    lock (m_rendererLock)
                    {
                        Render();
                    }
                }
            });

            // Run task on a dedicated high priority background thread.
            m_renderLoopWorker = ThreadPool.RunAsync(workItemHandler, WorkItemPriority.High, WorkItemOptions.TimeSliced);

        }

        public void OnSuspending()
        {
            // If the animation render loop is already running then do not start another thread.
            if (m_renderLoopWorker != null && m_renderLoopWorker.Status == AsyncStatus.Started)
            {
                m_renderLoopWorker.Cancel();
                m_renderLoopWorker = null;
            }
        }

        private void OnDpiChanged( DisplayInformation d, object o)
        {
            lock (m_rendererLock)
            {
                m_swapChain.WaitForGpu();
                m_ctx.ResetViewDependentResources();
                m_swapChain.SetDisplayInformation(d);
            }
        }

        private void M_swapChainPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            lock (m_rendererLock)
            {
                m_swapChain.WaitForGpu();
                m_ctx.ResetViewDependentResources();

                UniqueCreator.Graphics.Gpu.Size2D s;
                s.Width = (float)e.NewSize.Width;
                s.Height = (float)e.NewSize.Height;
                m_swapChain.SetLogicalSize(s);
            }
        }

        private void M_swapChainPanel_CompositionScaleChanged(SwapChainPanel sender, object args)
        {
            lock (m_rendererLock)
            {
                m_swapChain.WaitForGpu();
                m_ctx.ResetViewDependentResources();
                m_swapChain.SetCompositionScale(sender.CompositionScaleX, sender.CompositionScaleY);
            }
        }
        private void Render()
        {
            {
                var ctx                 = m_swapChain.CreateGraphicsComputeCommandContext();
                var backBuffer          = m_swapChain.BackBuffer;
                var size                = backBuffer.Size2D;
                var w                   = (uint)backBuffer.Size2D.Width;
                var h                   = (uint)backBuffer.Size2D.Height;

                var albedo              = m_ctx.CreateFrameColorBuffer(w, h, GraphicsFormat.R8G8B8A8_UNORM, ResourceState.RenderTarget);
                var depth               = m_ctx.CreateFrameDepthBuffer(w, h, DepthBufferFormat.Depth32Single, ResourceState.DepthWrite);

                ctx.SetRenderTarget(albedo, depth);
                ctx.SetDescriptorHeaps();
                ctx.Clear(albedo);
                ctx.Clear(depth);

                AlbedoPassData albedoPass;
                DepthPassData  depthPass;

                {
                    Size2D s = size;

                    {
                        ViewPort v;

                        v.MinDepth = 0.0f;
                        v.MaxDepth = 1.0f;
                        v.TopLeftX = 0.0f;
                        v.TopLeftY = 0.0f;
                        v.Width = s.Width;
                        v.Height = s.Height;

                        albedoPass.ViewPort.Width    = v.Width;
                        albedoPass.ViewPort.Height   = v.Height;
                        albedoPass.ViewPort.MinimumZ = v.MinDepth;
                        albedoPass.ViewPort.MaximumZ = v.MaxDepth;

                        depthPass.ViewPort.Width = v.Width;
                        depthPass.ViewPort.Height = v.Height;
                        depthPass.ViewPort.MinimumZ = v.MinDepth;
                        depthPass.ViewPort.MaximumZ = v.MaxDepth;

                        ctx.SetViewPort(v);
                    }

                    {
                        Rectangle2D v;

                        v.Left = 0;
                        v.Top = 0;
                        v.Right = s.Width;
                        v.Bottom = s.Height;

                        ctx.SetScissorRectangle(v);
                    }
                }


                albedoPass.Camera.ViewTransform         = CameraMatrix();
                albedoPass.Camera.PerspectiveTransform  = PerspectiveMatrix();
                depthPass.Camera.ViewTransform          = CameraMatrix();
                depthPass.Camera.PerspectiveTransform   = PerspectiveMatrix();

                //Depth prime the buffer
                {
                    m_mechanicMaterial.SubmitDepth(depthPass, ctx);
                    m_mechanicModel.SubmitDepth(ctx);
                    m_mechanicInstance.SubmitDepth(ctx);
                }

                //Read from the buffer, submit with depth test
                ctx.TransitionResource(depth, ResourceState.DepthWrite, ResourceState.DepthRead);

                {
                    //Submit per material data
                    m_mechanicMaterial.SubmitAlbedo(albedoPass, ctx);
                    //Submit Per Model Data
                    m_mechanicModel.SubmitAlbedo(ctx);
                    //Submit as many instances with different world matrices
                    m_mechanicInstance.SubmitAlbedo(ctx);
                }

                ctx.TransitionResource(albedo, ResourceState.RenderTarget, ResourceState.CopySource);
                ctx.TransitionResource(backBuffer, ResourceState.Present, ResourceState.CopyDestination);
                ctx.CopyResource(backBuffer, albedo);
                ctx.TransitionResource(backBuffer, ResourceState.CopyDestination, ResourceState.Present);
                ctx.Submit();
            }
           
            m_swapChain.Present();

            m_swapChain.MoveToNextFrame();

            //flush all buffers
            m_ctx.Sync();
            m_swapChain.Sync();
        }
    }
}
