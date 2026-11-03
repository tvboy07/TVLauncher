package net.tvlauncher.cosmetics;

import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.ArrayList;
import java.util.List;
import net.minecraft.client.render.entity.model.PlayerCapeModel;
import net.minecraft.client.util.math.MatrixStack;
import org.junit.jupiter.api.Test;

class CapeModelUvTest {
    @Test
    void capeUvsStayInsideTheTopLeftCorner() {
        var model = new PlayerCapeModel(PlayerCapeModel.getTexturedModelData().createModel());
        var cape = model.getRootPart().getChild("body").getChild("cape");
        assertNotNull(cape);

        List<Float> us = new ArrayList<>();
        List<Float> vs = new ArrayList<>();
        cape.forEachCuboid(new MatrixStack(), (entry, name, index, cuboid) -> {
            System.out.println("cuboid " + index + " name=" + name
                + " min=" + cuboid.minX + "," + cuboid.minY + "," + cuboid.minZ
                + " max=" + cuboid.maxX + "," + cuboid.maxY + "," + cuboid.maxZ);
            for (var side : cuboid.sides) {
                var sideUs = new ArrayList<Float>();
                var sideVs = new ArrayList<Float>();
                for (var vertex : side.vertices()) {
                    us.add(vertex.u());
                    vs.add(vertex.v());
                    sideUs.add(vertex.u());
                    sideVs.add(vertex.v());
                }
                System.out.printf("  side u %.4f..%.4f v %.4f..%.4f%n",
                    sideUs.stream().min(Float::compare).orElseThrow(),
                    sideUs.stream().max(Float::compare).orElseThrow(),
                    sideVs.stream().min(Float::compare).orElseThrow(),
                    sideVs.stream().max(Float::compare).orElseThrow());
            }
        });

        float minU = us.stream().min(Float::compare).orElseThrow();
        float maxU = us.stream().max(Float::compare).orElseThrow();
        float minV = vs.stream().min(Float::compare).orElseThrow();
        float maxV = vs.stream().max(Float::compare).orElseThrow();
        System.out.printf("cape UV: u %.4f..%.4f, v %.4f..%.4f%n", minU, maxU, minV, maxV);

        assertTrue(minU >= 0f && maxU <= 0.5f, "u fuera del atlas 64x32");
        assertTrue(minV >= 0f && maxV <= 0.55f, "v fuera del atlas 64x32");
    }
}
