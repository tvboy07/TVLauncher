package net.tvlauncher.cosmetics;

import java.util.List;
import java.util.Map;
import java.util.Optional;
import net.minecraft.client.model.Model;
import net.minecraft.client.model.ModelPart;
import net.minecraft.client.model.ModelPartBuilder;
import net.minecraft.client.render.OverlayTexture;
import net.minecraft.client.render.RenderLayers;
import net.minecraft.client.render.command.OrderedRenderCommandQueue;
import net.minecraft.client.render.entity.feature.FeatureRenderer;
import net.minecraft.client.render.entity.feature.FeatureRendererContext;
import net.minecraft.client.render.entity.model.PlayerCapeModel;
import net.minecraft.client.render.entity.model.PlayerEntityModel;
import net.minecraft.client.render.entity.state.PlayerEntityRenderState;
import net.minecraft.client.util.math.MatrixStack;
import org.joml.Quaternionf;

/**
 * Dibuja la capa y el sombrero TVLauncher sobre cada jugador visible.
 *
 * <p>La capa reutiliza {@link PlayerCapeModel}: es la misma geometría y la misma
 * animación que la capa vanilla, pero con la textura del mod. El sombrero se
 * dibuja con una geometría propia anclada a la transformación de la cabeza.
 */
public final class TvCosmeticsFeatureRenderer
    extends FeatureRenderer<PlayerEntityRenderState, PlayerEntityModel> {

    private static final float DEGREES_TO_RADIANS = 0.017453292f;
    private static final float HAT_TILT_RADIANS = 2.0f * DEGREES_TO_RADIANS;

    private final PlayerCapeModel capeModel;
    private final HatModel hatModel;

    public TvCosmeticsFeatureRenderer(FeatureRendererContext<PlayerEntityRenderState, PlayerEntityModel> context) {
        super(context);
        this.capeModel = new PlayerCapeModel(PlayerCapeModel.getTexturedModelData().createModel());
        this.hatModel = HatModel.create();
    }

    @Override
    public void render(
            MatrixStack matrices,
            OrderedRenderCommandQueue queue,
            int light,
            PlayerEntityRenderState state,
            float limbAngle,
            float limbDistance) {
        if (state.invisible) {
            return;
        }

        Optional<ResolvedCosmetics> resolved = CosmeticRenderState.resolve(state.id);
        if (resolved.isEmpty()) {
            return;
        }

        ResolvedCosmetics cosmetics = resolved.get();
        if (cosmetics.hasCape() && state.glidingTicks <= 0.0f) {
            matrices.push();
            queue.submitModel(
                capeModel,
                state,
                matrices,
                CosmeticRenderLayers.cape(),
                light,
                OverlayTexture.DEFAULT_UV,
                state.outlineColor,
                null);
            matrices.pop();
        }

        if (cosmetics.hasHat()) {
            matrices.push();
            PlayerEntityModel model = getContextModel();
            model.getRootPart().applyTransform(matrices);
            model.applyTransform(matrices);
            matrices.multiply(new Quaternionf().rotateX(HAT_TILT_RADIANS));
            queue.submitModel(
                hatModel,
                state,
                matrices,
                CosmeticRenderLayers.hat(),
                light,
                OverlayTexture.DEFAULT_UV,
                state.outlineColor,
                null);
            matrices.pop();
        }
    }

    /**
     * Geometría del sombrero en unidades de modelo, anclada al origen de la
     * cabeza. La copa se apoya sobre la corona del jugador y la cinta rodea su
     * base.
     */
    private static final class HatModel extends Model<PlayerEntityRenderState> {
        private static final int TEXTURE_SIZE = 64;

        private HatModel(ModelPart root) {
            super(root, RenderLayers::entityCutout);
        }

        private static HatModel create() {
            List<ModelPart.Cuboid> cuboids = ModelPartBuilder.create()
                .uv(0, 0)
                .cuboid(-3.1f, -11.3f, -3.1f, 6.2f, 2.8f, 6.2f)
                .uv(0, 16)
                .cuboid(-4.6f, -8.5f, -4.6f, 9.2f, 0.5f, 9.2f)
                .uv(0, 32)
                .cuboid(-3.175f, -9.0f, -3.175f, 6.35f, 0.5f, 6.35f)
                .build()
                .stream()
                .map(data -> data.createCuboid(TEXTURE_SIZE, TEXTURE_SIZE))
                .toList();

            return new HatModel(new ModelPart(cuboids, Map.of()));
        }
    }
}
