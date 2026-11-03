package net.tvlauncher.cosmetics.mixin;

import net.minecraft.client.render.command.OrderedRenderCommandQueue;
import net.minecraft.client.render.entity.feature.CapeFeatureRenderer;
import net.minecraft.client.render.entity.state.PlayerEntityRenderState;
import net.minecraft.client.util.math.MatrixStack;
import net.tvlauncher.cosmetics.CosmeticRenderState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * Evita que la capa vanilla del jugador se dibuje encima de la capa TVLauncher.
 */
@Mixin(CapeFeatureRenderer.class)
public abstract class CapeFeatureRendererMixin {
    @Inject(
        method = "render(Lnet/minecraft/client/util/math/MatrixStack;"
            + "Lnet/minecraft/client/render/command/OrderedRenderCommandQueue;"
            + "ILnet/minecraft/client/render/entity/state/PlayerEntityRenderState;FF)V",
        at = @At("HEAD"),
        cancellable = true)
    private void tvl$cancelVanillaCape(
            MatrixStack matrices,
            OrderedRenderCommandQueue queue,
            int light,
            PlayerEntityRenderState state,
            float limbAngle,
            float limbDistance,
            CallbackInfo ci) {
        if (CosmeticRenderState.hasCustomCape(state.id)) {
            ci.cancel();
        }
    }
}
