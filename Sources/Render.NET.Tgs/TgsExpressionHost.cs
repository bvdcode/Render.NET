namespace Render.NET.Tgs
{
    internal static class TgsExpressionHost
    {
        internal const string Source = """
            var scene=JSON.parse(sceneJson), propertyData=JSON.parse(propertyJson);
            var time=currentFrame/scene.rate;
            function property(id,initial) {
                function at(t,original) { return id<0 ? initial : JSON.parse(readProperty(id,t,original)); }
                var p={
                    get value() { return at(time,false); },
                    valueAtTime:function(t) { return at(t,false); },
                    velocityAtTime:function(t) { return $bm_div($bm_sub(at(t,false),at(t-0.001,false)),0.001); },
                    valueOf:function() { var v=at(time,false); return Array.isArray(v) ? v[0] : v; },
                    toJSON:function() { return at(time,false); }
                };
                return new Proxy(p,{get:function(target,name) {
                    if(typeof name==='string' && /^\d+$/.test(name)) return at(time,false)[Number(name)];
                    return target[name];
                }});
            }
            var layers=scene.layers.map(function(data) {
                var properties=data.transform;
                function transform(name) {
                    var names={'ADBE Anchor Point':'a','ADBE Position':'p','ADBE Scale':'s','ADBE Rotate Z':'r','ADBE Opacity':'o'};
                    var id=names[name];
                    if(!id) throw new ReferenceError('Unknown transform property '+name);
                    return getTransform(id);
                }
                function getTransform(id) {
                    var defaults={a:[0,0,0],p:[0,0,0],s:[100,100,100],r:0,o:100,sk:0,sa:0};
                    return property(properties[id]===undefined ? -1 : properties[id], defaults[id]);
                }
                Object.defineProperties(transform,{
                    anchorPoint:{get:()=>getTransform('a')},position:{get:()=>getTransform('p')},
                    scale:{get:()=>getTransform('s')},rotation:{get:()=>getTransform('r')},opacity:{get:()=>getTransform('o')}
                });
                function layer(name) {
                    if(name==='ADBE Transform Group') return transform;
                    throw new ReferenceError('Unknown layer property '+name);
                }
                layer.transform=transform;
                layer.index=data.index+1;
                Object.defineProperty(layer,'name',{value:data.name});
                layer.position=getTransform('p'); layer.anchorPoint=getTransform('a');
                layer.rotation=getTransform('r'); layer.scale=getTransform('s');
                layer.effect=function(name) {
                    var effect=typeof name==='number' ? data.effects[name-1] : data.effects.find(e=>e.name===name || e.matchName===name);
                    if(!effect) throw new ReferenceError('Missing effect '+name);
                    return function(key) {
                        var p=typeof key==='number' ? effect.properties[key-1] : effect.properties.find(p=>p.name===key || p.matchName===key);
                        if(!p || p.id<0) throw new ReferenceError('Missing effect property '+key);
                        return property(p.id);
                    };
                };
                layer.toComp=function(point) { return JSON.parse(mapPoint(data.index,JSON.stringify(raw(point)))); };
                return layer;
            });
            scene.layers.forEach(function(data,i) {
                layers[i].parent=data.parent<0 ? undefined : layers[data.parent];
                layers[i].hasParent=data.parent>=0;
            });
            var thisComp={width:scene.width,height:scene.height,frameDuration:1/scene.rate,
                layer:function(name) {
                    var result=typeof name==='number' ? layers[name-1] : layers.find(l=>l.name===name);
                    if(!result) throw new ReferenceError('Missing composition layer '+name);
                    return result;
                }
            };
            var thisLayer=layers[currentLayer];
            var thisProperty=property(currentId);
            function effect(name) { return thisLayer.effect(name); }
            function valueAtTime(t) { return JSON.parse(readProperty(currentId,t,true)); }
            function velocityAtTime(t) { return $bm_div($bm_sub(valueAtTime(t),valueAtTime(t-0.001)),0.001); }
            var value=valueAtTime(time), numKeys=propertyData.keys.length;
            function key(index) {
                if(index<1 || index>numKeys || index!==Math.floor(index)) throw new RangeError('Key index out of range');
                return {time:propertyData.keys[index-1]/scene.rate,index:index,value:valueAtTime(propertyData.keys[index-1]/scene.rate)};
            }
            function nearestKey(t) {
                if(!numKeys) throw new RangeError('No keyframes');
                var index=0;
                for(var i=1;i<numKeys;i++) {
                    if(Math.abs(propertyData.keys[i]/scene.rate-t)<Math.abs(propertyData.keys[index]/scene.rate-t)) index=i;
                }
                return key(index+1);
            }
            function framesToTime(frame) { return frame/scene.rate; }
            function timeToFrames(t) { return (arguments.length ? t : time)*scene.rate; }
            """;
    }
}
