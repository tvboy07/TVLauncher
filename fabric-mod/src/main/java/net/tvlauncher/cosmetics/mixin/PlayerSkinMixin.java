package net.tvlauncher.cosmetics.mixin;

import net.minecraft.client.network.AbstractClientPlayerEntity;
import net.minecraft.entity.player.SkinTextures;
import net.tvlauncher.cosmetics.CosmeticRenderState;
import net.tvlauncher.cosmetics.LocalSkinOverride;
import net.tvlauncher.cosmetics.TvCosmeticsClient;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Sustituye la skin del cuerpo en {@link AbstractClientPlayerEntity#getSkin()}.
 *
 * <p>Este método lo usan tanto el renderer de tercera persona como las manos
 * en primera persona. Solo se toca al jugador local; la capa se aplica en
 * {@link PlayerEntityRendererMixin} una vez por frame.
 */
@Mixin(AbstractClientPlayerEntity.class)
public abstract class PlayerSkinMixin {
    private static boolean tvl$loggedLocalSkin;

    @Inject(method = "getSkin", at = @At("RETURN"), cancellable = true)
    private void tvl$overrideSkin(CallbackInfoReturnable<SkinTextures> cir) {
        if (!LocalSkinOverride.isActive()) {
            return;
        }

        AbstractClientPlayerEntity self = (AbstractClientPlayerEntity) (Object) this;
        if (!CosmeticRenderState.isLocalPlayer(self.getId())) {
            return;
        }

        if (!tvl$loggedLocalSkin) {
            tvl$loggedLocalSkin = true;
            TvCosmeticsClient.LOGGER.info("Local skin override applied to entity {}", self.getId());
        }
        cir.setReturnValue(LocalSkinOverride.applyLocal(cir.getReturnValue()));
    }
}
