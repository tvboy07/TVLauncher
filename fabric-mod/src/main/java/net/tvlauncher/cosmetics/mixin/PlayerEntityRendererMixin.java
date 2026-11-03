package net.tvlauncher.cosmetics.mixin;

import net.minecraft.client.render.entity.PlayerEntityRenderer;
import net.minecraft.client.render.entity.state.PlayerEntityRenderState;
import net.minecraft.entity.PlayerLikeEntity;
import net.tvlauncher.cosmetics.CosmeticRenderLayers;
import net.tvlauncher.cosmetics.CosmeticRenderState;
import net.tvlauncher.cosmetics.LocalSkinOverride;
import net.tvlauncher.cosmetics.TvCosmeticsClient;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * Inyecta la textura de la capa TVL en el render state del jugador. El
 * {@code CapeFeatureRenderer} vanilla se encarga de dibujarla con el modelo y
 * la animación correctos.
 */
@Mixin(PlayerEntityRenderer.class)
public abstract class PlayerEntityRendererMixin {
    private static boolean tvl$loggedCape;

    @Inject(
        method = "updateRenderState(Lnet/minecraft/entity/PlayerLikeEntity;"
            + "Lnet/minecraft/client/render/entity/state/PlayerEntityRenderState;F)V",
        at = @At("TAIL"))
    private void tvl$applyCape(
            PlayerLikeEntity entity,
            PlayerEntityRenderState state,
            float tickDelta,
            CallbackInfo ci) {
        if (!CosmeticRenderState.hasCustomCape(state.id)) {
            return;
        }

        if (!tvl$loggedCape) {
            tvl$loggedCape = true;
            TvCosmeticsClient.LOGGER.info("TVL cape override applied to entity {}", state.id);
        }
        state.skinTextures = LocalSkinOverride.applyCape(
            state.skinTextures, CosmeticRenderLayers.CAPE_TEXTURE);
    }
}
